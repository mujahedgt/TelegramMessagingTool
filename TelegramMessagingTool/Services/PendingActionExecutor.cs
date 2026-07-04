using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.Json;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Tools.CommandExecution;
using TelegramMessagingTool.Tools.GitHub;

namespace TelegramMessagingTool.Services;

public sealed class PendingActionExecutor
{
    private readonly IProcessTerminator _processTerminator;
    private readonly DocumentStorageService _documentStorage;
    private readonly ILatestReleaseRestarter _latestReleaseRestarter;
    private readonly IGitHubIssueCreator _gitHubIssueCreator;
    private readonly IGitHubIssueCommenter _gitHubIssueCommenter;

    public PendingActionExecutor(
        IProcessTerminator processTerminator,
        DocumentStorageService documentStorage,
        ILatestReleaseRestarter? latestReleaseRestarter = null,
        IGitHubIssueCreator? gitHubIssueCreator = null,
        IGitHubIssueCommenter? gitHubIssueCommenter = null)
    {
        _processTerminator = processTerminator;
        _documentStorage = documentStorage;
        _latestReleaseRestarter = latestReleaseRestarter ?? new SystemLatestReleaseRestarter();
        _gitHubIssueCreator = gitHubIssueCreator ?? new SystemGitHubIssueCreator();
        _gitHubIssueCommenter = gitHubIssueCommenter ?? new SystemGitHubIssueCommenter();
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
            "publish_release" => await ExecutePublishReleaseAsync(action, cancellationToken),
            "restart_latest_bot" => await ExecuteRestartLatestBotAsync(action, cancellationToken),
            "repo_replace_text" => await ExecuteRepoReplaceTextAsync(action, cancellationToken),
            "repo_apply_patch" => await ExecuteRepoApplyPatchAsync(action, cancellationToken),
            "repo_commit_changes" => await ExecuteRepoCommitChangesAsync(action, cancellationToken),
            "repo_push_changes" => await ExecuteRepoPushChangesAsync(action, cancellationToken),
            "github_create_issue" => await ExecuteGitHubCreateIssueAsync(action, cancellationToken),
            "github_comment_issue" => await ExecuteGitHubCommentIssueAsync(action, cancellationToken),
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

    private static async Task<PendingActionExecutionResult> ExecutePublishReleaseAsync(
        PendingAction action,
        CancellationToken cancellationToken)
    {
        if (!TryReadPublishReleasePayload(action.PayloadJson, out PublishReleasePayload payload, out string error))
        {
            return PendingActionExecutionResult.Failed(error);
        }

        ReleasePublishResult publishResult = await ReleasePublishExecutor.PublishAsync(payload, cancellationToken);
        return publishResult.Success
            ? PendingActionExecutionResult.Completed(publishResult.Message)
            : PendingActionExecutionResult.Failed(publishResult.Message);
    }

    private static bool TryReadPublishReleasePayload(string payloadJson, out PublishReleasePayload payload, out string error)
    {
        payload = PublishReleasePayload.Empty;
        error = string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            JsonElement root = document.RootElement;
            string action = ReadPayloadString(root, "action");
            string projectRoot = ReadPayloadString(root, "project_root");
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                projectRoot = ReadPayloadString(root, "projectRoot");
            }

            string reason = ReadPayloadString(root, "reason");
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                error = "Execution failed: publish_release payload is missing project_root.";
                return false;
            }

            payload = new PublishReleasePayload(action, projectRoot, reason, DateTime.UtcNow);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Execution failed: invalid publish_release payload JSON. {ex.Message}";
            return false;
        }
    }

    private async Task<PendingActionExecutionResult> ExecuteRestartLatestBotAsync(
        PendingAction action,
        CancellationToken cancellationToken)
    {
        if (!TryReadRestartLatestBotPayload(action.PayloadJson, out RestartLatestBotPayload payload, out string error))
        {
            return PendingActionExecutionResult.Failed(error);
        }

        LatestReleaseRestartResult restartResult = await _latestReleaseRestarter.RestartAsync(payload, cancellationToken);
        return restartResult.Success
            ? PendingActionExecutionResult.Completed(restartResult.Message)
            : PendingActionExecutionResult.Failed(restartResult.Message);
    }

    private static bool TryReadRestartLatestBotPayload(string payloadJson, out RestartLatestBotPayload payload, out string error)
    {
        payload = RestartLatestBotPayload.Empty;
        error = string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            JsonElement root = document.RootElement;
            string action = ReadPayloadString(root, "action");
            string projectRoot = ReadPayloadString(root, "project_root");
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                projectRoot = ReadPayloadString(root, "projectRoot");
            }

            string reason = ReadPayloadString(root, "reason");
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                error = "Execution failed: restart_latest_bot payload is missing project_root.";
                return false;
            }

            payload = new RestartLatestBotPayload(action, projectRoot, reason, DateTime.UtcNow);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Execution failed: invalid restart_latest_bot payload JSON. {ex.Message}";
            return false;
        }
    }

    private static string ReadPayloadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;
    }

    private static async Task<PendingActionExecutionResult> ExecuteRepoReplaceTextAsync(
        PendingAction action,
        CancellationToken cancellationToken)
    {
        if (!TryReadRepoReplaceTextPayload(action.PayloadJson, out RepoReplaceTextPayload payload, out string error))
        {
            return PendingActionExecutionResult.Failed(error);
        }

        if (!RepoWritePathPolicy.TryResolveProjectFile(payload.ProjectRoot, payload.Path, out string resolvedPath, out error))
        {
            return PendingActionExecutionResult.Failed($"Execution refused: {error}");
        }

        if (!string.Equals(Path.GetFullPath(payload.FullPath), resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            return PendingActionExecutionResult.Failed("Execution refused: payload full path does not match the resolved safe project path.");
        }

        if (!File.Exists(resolvedPath))
        {
            return PendingActionExecutionResult.Failed($"Execution failed: file does not exist: {payload.Path}");
        }

        string content = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        int firstIndex = content.IndexOf(payload.OldText, StringComparison.Ordinal);
        if (firstIndex < 0)
        {
            return PendingActionExecutionResult.Failed($"Execution failed: old_text was not found in {payload.Path}.");
        }

        if (content.IndexOf(payload.OldText, firstIndex + payload.OldText.Length, StringComparison.Ordinal) >= 0)
        {
            return PendingActionExecutionResult.Failed($"Execution refused: old_text appears more than once in {payload.Path}. Use a more specific replacement.");
        }

        string updated = content.Replace(payload.OldText, payload.NewText, StringComparison.Ordinal);
        await File.WriteAllTextAsync(resolvedPath, updated, cancellationToken);
        return PendingActionExecutionResult.Completed($"Replaced text in {payload.Path}. Run tests before committing.");
    }

    private static bool TryReadRepoReplaceTextPayload(string payloadJson, out RepoReplaceTextPayload payload, out string error)
    {
        payload = RepoReplaceTextPayload.Empty;
        error = string.Empty;

        try
        {
            RepoReplaceTextPayload? parsed = JsonSerializer.Deserialize<RepoReplaceTextPayload>(payloadJson, JsonOptions);
            if (parsed is null
                || string.IsNullOrWhiteSpace(parsed.ProjectRoot)
                || string.IsNullOrWhiteSpace(parsed.Path)
                || string.IsNullOrEmpty(parsed.OldText))
            {
                error = "Execution failed: repo_replace_text payload is missing project_root, path, or old_text.";
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Execution failed: invalid repo_replace_text payload JSON. {ex.Message}";
            return false;
        }
    }

    private static async Task<PendingActionExecutionResult> ExecuteRepoApplyPatchAsync(
        PendingAction action,
        CancellationToken cancellationToken)
    {
        if (!TryReadRepoApplyPatchPayload(action.PayloadJson, out RepoApplyPatchPayload payload, out string error))
        {
            return PendingActionExecutionResult.Failed(error);
        }

        RepoPatchApplyResult applyResult = await RepoPatchApplyExecutor.ApplyAsync(payload, cancellationToken);
        return applyResult.Success
            ? PendingActionExecutionResult.Completed(applyResult.Message)
            : PendingActionExecutionResult.Failed(applyResult.Message);
    }

    private static bool TryReadRepoApplyPatchPayload(string payloadJson, out RepoApplyPatchPayload payload, out string error)
    {
        payload = RepoApplyPatchPayload.Empty;
        error = string.Empty;

        try
        {
            RepoApplyPatchPayload? parsed = JsonSerializer.Deserialize<RepoApplyPatchPayload>(payloadJson, JsonOptions);
            if (parsed is null
                || string.IsNullOrWhiteSpace(parsed.ProjectRoot)
                || string.IsNullOrWhiteSpace(parsed.Patch)
                || parsed.AffectedPaths is null
                || parsed.AffectedPaths.Count == 0)
            {
                error = "Execution failed: repo_apply_patch payload is missing project_root, patch, or affected_paths.";
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Execution failed: invalid repo_apply_patch payload JSON. {ex.Message}";
            return false;
        }
    }

    private static async Task<PendingActionExecutionResult> ExecuteRepoCommitChangesAsync(
        PendingAction action,
        CancellationToken cancellationToken)
    {
        if (!TryReadRepoCommitChangesPayload(action.PayloadJson, out RepoCommitChangesPayload payload, out string error))
        {
            return PendingActionExecutionResult.Failed(error);
        }

        RepoGitCommitResult commitResult = await RepoGitCommitExecutor.CommitAsync(payload, cancellationToken);
        return commitResult.Success
            ? PendingActionExecutionResult.Completed(commitResult.Message)
            : PendingActionExecutionResult.Failed(commitResult.Message);
    }

    private static bool TryReadRepoCommitChangesPayload(string payloadJson, out RepoCommitChangesPayload payload, out string error)
    {
        payload = RepoCommitChangesPayload.Empty;
        error = string.Empty;

        try
        {
            RepoCommitChangesPayload? parsed = JsonSerializer.Deserialize<RepoCommitChangesPayload>(payloadJson, JsonOptions);
            if (parsed is null
                || string.IsNullOrWhiteSpace(parsed.ProjectRoot)
                || string.IsNullOrWhiteSpace(parsed.Message))
            {
                error = "Execution failed: repo_commit_changes payload is missing project_root or message.";
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Execution failed: invalid repo_commit_changes payload JSON. {ex.Message}";
            return false;
        }
    }

    private static async Task<PendingActionExecutionResult> ExecuteRepoPushChangesAsync(
        PendingAction action,
        CancellationToken cancellationToken)
    {
        if (!TryReadRepoPushChangesPayload(action.PayloadJson, out RepoPushChangesPayload payload, out string error))
        {
            return PendingActionExecutionResult.Failed(error);
        }

        RepoGitPushResult pushResult = await RepoGitPushExecutor.PushAsync(payload, cancellationToken);
        return pushResult.Success
            ? PendingActionExecutionResult.Completed(pushResult.Message)
            : PendingActionExecutionResult.Failed(pushResult.Message);
    }

    private static bool TryReadRepoPushChangesPayload(string payloadJson, out RepoPushChangesPayload payload, out string error)
    {
        payload = RepoPushChangesPayload.Empty;
        error = string.Empty;

        try
        {
            RepoPushChangesPayload? parsed = JsonSerializer.Deserialize<RepoPushChangesPayload>(payloadJson, JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.ProjectRoot))
            {
                error = "Execution failed: repo_push_changes payload is missing project_root.";
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Execution failed: invalid repo_push_changes payload JSON. {ex.Message}";
            return false;
        }
    }

    private async Task<PendingActionExecutionResult> ExecuteGitHubCreateIssueAsync(
        PendingAction action,
        CancellationToken cancellationToken)
    {
        if (!TryReadGitHubCreateIssuePayload(action.PayloadJson, out GitHubCreateIssuePayload payload, out string error))
        {
            return PendingActionExecutionResult.Failed(error);
        }

        GitHubIssueCreateResult createResult = await _gitHubIssueCreator.CreateIssueAsync(payload, cancellationToken);
        return createResult.Success
            ? PendingActionExecutionResult.Completed(createResult.Message)
            : PendingActionExecutionResult.Failed(createResult.Message);
    }

    private static bool TryReadGitHubCreateIssuePayload(string payloadJson, out GitHubCreateIssuePayload payload, out string error)
    {
        payload = GitHubCreateIssuePayload.Empty;
        error = string.Empty;

        try
        {
            GitHubCreateIssuePayload? parsed = JsonSerializer.Deserialize<GitHubCreateIssuePayload>(payloadJson, JsonOptions);
            if (parsed is null
                || string.IsNullOrWhiteSpace(parsed.Owner)
                || string.IsNullOrWhiteSpace(parsed.Repo)
                || string.IsNullOrWhiteSpace(parsed.Title))
            {
                error = "Execution failed: github_create_issue payload is missing owner, repo, or title.";
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Execution failed: invalid github_create_issue payload JSON. {ex.Message}";
            return false;
        }
    }

    private async Task<PendingActionExecutionResult> ExecuteGitHubCommentIssueAsync(
        PendingAction action,
        CancellationToken cancellationToken)
    {
        if (!TryReadGitHubCommentIssuePayload(action.PayloadJson, out GitHubCommentIssuePayload payload, out string error))
        {
            return PendingActionExecutionResult.Failed(error);
        }

        GitHubIssueCommentResult commentResult = await _gitHubIssueCommenter.CommentAsync(payload, cancellationToken);
        return commentResult.Success
            ? PendingActionExecutionResult.Completed(commentResult.Message)
            : PendingActionExecutionResult.Failed(commentResult.Message);
    }

    private static bool TryReadGitHubCommentIssuePayload(string payloadJson, out GitHubCommentIssuePayload payload, out string error)
    {
        payload = GitHubCommentIssuePayload.Empty;
        error = string.Empty;

        try
        {
            GitHubCommentIssuePayload? parsed = JsonSerializer.Deserialize<GitHubCommentIssuePayload>(payloadJson, JsonOptions);
            if (parsed is null
                || string.IsNullOrWhiteSpace(parsed.Owner)
                || string.IsNullOrWhiteSpace(parsed.Repo)
                || parsed.Number <= 0
                || string.IsNullOrWhiteSpace(parsed.Body))
            {
                error = "Execution failed: github_comment_issue payload is missing owner, repo, number, or body.";
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Execution failed: invalid github_comment_issue payload JSON. {ex.Message}";
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
