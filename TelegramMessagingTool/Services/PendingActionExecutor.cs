using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.Json;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class PendingActionExecutor
{
    private readonly IProcessTerminator _processTerminator;
    private readonly DocumentStorageService _documentStorage;

    public PendingActionExecutor(IProcessTerminator processTerminator, DocumentStorageService documentStorage)
    {
        _processTerminator = processTerminator;
        _documentStorage = documentStorage;
    }

    public async Task<PendingActionExecutionResult> ExecuteApprovedAsync(
        TelegramDbContext dbContext,
        PendingAction action,
        CancellationToken cancellationToken)
    {
        if (action.Status != PendingActionStatuses.Approved)
        {
            return PendingActionExecutionResult.Skipped($"Action #{action.Id} is {action.Status}; only approved actions can execute.");
        }

        PendingActionExecutionResult result = action.ToolName switch
        {
            "kill_process" => ExecuteKillProcess(action),
            "delete_file" => await ExecuteDeleteFileAsync(dbContext, action, cancellationToken),
            _ => PendingActionExecutionResult.Skipped($"No automatic execution is registered for action type '{action.ToolName}'.")
        };

        action.DecisionNote = result.Message;
        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    private PendingActionExecutionResult ExecuteKillProcess(PendingAction action)
    {
        if (!TryReadPid(action.PayloadJson, out int processId, out string error))
        {
            return PendingActionExecutionResult.Failed(error);
        }

        ProcessTerminationResult terminationResult = _processTerminator.Terminate(processId);
        return terminationResult.Success
            ? PendingActionExecutionResult.Completed(terminationResult.Message)
            : PendingActionExecutionResult.Failed(terminationResult.Message);
    }

    private async Task<PendingActionExecutionResult> ExecuteDeleteFileAsync(
        TelegramDbContext dbContext,
        PendingAction action,
        CancellationToken cancellationToken)
    {
        if (!TryReadFileId(action.PayloadJson, out int fileId, out string error))
        {
            return PendingActionExecutionResult.Failed(error);
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == fileId && x.ConnectedUserId == action.ConnectedUserId, cancellationToken);

        if (file is null)
        {
            return PendingActionExecutionResult.Failed($"Execution failed: file #{fileId} was not found for this user.");
        }

        string fileName = file.OriginalFileName;
        FileDeletionResult deletionResult = _documentStorage.DeleteStoredFile(file);
        if (!deletionResult.Success)
        {
            return PendingActionExecutionResult.Failed(deletionResult.Message);
        }

        dbContext.UploadedFiles.Remove(file);
        await dbContext.SaveChangesAsync(cancellationToken);
        return PendingActionExecutionResult.Completed($"Deleted file #{fileId}: {fileName}. {deletionResult.Message}");
    }

    private static bool TryReadFileId(string payloadJson, out int fileId, out string error)
    {
        fileId = 0;
        error = string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty("file_id", out JsonElement fileIdElement)
                || !fileIdElement.TryGetInt32(out fileId)
                || fileId <= 0)
            {
                error = "Execution failed: delete_file payload does not contain a valid positive file_id.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Execution failed: invalid delete_file payload JSON. {ex.Message}";
            return false;
        }
    }

    private static bool TryReadPid(string payloadJson, out int processId, out string error)
    {
        processId = 0;
        error = string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty("pid", out JsonElement pidElement)
                || !pidElement.TryGetInt32(out processId)
                || processId <= 0)
            {
                error = "Execution failed: kill_process payload does not contain a valid positive PID.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Execution failed: invalid kill_process payload JSON. {ex.Message}";
            return false;
        }
    }
}

public interface IProcessTerminator
{
    ProcessTerminationResult Terminate(int processId);
}

public sealed class SystemProcessTerminator : IProcessTerminator
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "Idle",
        "Registry",
        "smss",
        "csrss",
        "wininit",
        "winlogon",
        "services",
        "lsass",
        "svchost",
        "explorer"
    };

    public ProcessTerminationResult Terminate(int processId)
    {
        if (processId <= 0)
        {
            return ProcessTerminationResult.Failed("Execution failed: PID must be a positive integer.");
        }

        int currentProcessId = Environment.ProcessId;
        if (processId == currentProcessId)
        {
            return ProcessTerminationResult.Failed("Execution refused: the bot will not terminate its own process.");
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            string processName = process.ProcessName;
            if (ProtectedProcessNames.Contains(processName))
            {
                return ProcessTerminationResult.Failed($"Execution refused: '{processName}' is treated as a protected system process.");
            }

            process.Kill(entireProcessTree: false);
            bool exited = process.WaitForExit(5000);
            return exited
                ? ProcessTerminationResult.Ok($"Process PID {processId} ({processName}) was terminated successfully.")
                : ProcessTerminationResult.Failed($"Termination was requested for PID {processId} ({processName}), but it did not exit within 5 seconds.");
        }
        catch (ArgumentException)
        {
            return ProcessTerminationResult.Ok($"Process PID {processId} is not running anymore.");
        }
        catch (InvalidOperationException ex)
        {
            return ProcessTerminationResult.Failed($"Execution failed: process PID {processId} could not be accessed. {ex.Message}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return ProcessTerminationResult.Failed($"Execution failed: permission denied or OS refused to terminate PID {processId}. {ex.Message}");
        }
    }
}

public sealed record ProcessTerminationResult(bool Success, string Message)
{
    public static ProcessTerminationResult Ok(string message) => new(true, message);

    public static ProcessTerminationResult Failed(string message) => new(false, message);
}

public sealed record PendingActionExecutionResult(bool Executed, bool Success, string Message)
{
    public static PendingActionExecutionResult Completed(string message) => new(true, true, message);

    public static PendingActionExecutionResult Failed(string message) => new(true, false, message);

    public static PendingActionExecutionResult Skipped(string message) => new(false, true, message);
}
