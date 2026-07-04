using System.Diagnostics;
using System.Text.Json;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Tools.CommandExecution;

public abstract class ReleaseActionRequestTool : IApprovalRequestTool
{
    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;
    private readonly string _projectRoot;
    private readonly string _actionName;
    private readonly string _actionDescription;

    protected ReleaseActionRequestTool(
        PendingActionService pendingActionService,
        BotSettings settings,
        string projectRoot,
        string actionName,
        string actionDescription)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
        _projectRoot = Path.GetFullPath(projectRoot);
        _actionName = actionName;
        _actionDescription = actionDescription;
    }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public bool RequiresApproval => true;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Fail(
            $"{Name} requires an authenticated pending-action context. Use it from the Telegram/console agent flow so it can create an approval request, or use /pending to review existing requests."));
    }

    public async Task<ToolResult> CreatePendingActionAsync(
        string input,
        TelegramDbContext dbContext,
        ConnectedUser user,
        CancellationToken cancellationToken)
    {
        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return ToolResult.Fail(BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId));
        }

        if (!Directory.Exists(_projectRoot))
        {
            return ToolResult.Fail($"Project root does not exist: {_projectRoot}");
        }

        if (!TryParseReason(input, out string reason, out string error))
        {
            return ToolResult.Fail(error);
        }

        string payloadJson = JsonSerializer.Serialize(new
        {
            action = _actionName,
            project_root = _projectRoot,
            reason,
            requested_at_utc = DateTime.UtcNow
        });

        PendingAction pendingAction = await _pendingActionService.CreateAsync(
            dbContext,
            user,
            toolName: _actionName,
            description: $"{_actionDescription} Project root: {_projectRoot}. Reason: {reason}",
            payloadJson: payloadJson,
            riskLevel: "high",
            ttl: TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken);

        return ToolResult.Ok($"""
Approval required.

Pending action #{pendingAction.Id}
Type: {_actionName}
Risk: high
Expires UTC: {pendingAction.ExpiresAt:yyyy-MM-dd HH:mm}

This request only created a pending action. It did not publish, restart, stop, or modify the bot runtime.
Approve: /approve {pendingAction.Id}
Deny: /deny {pendingAction.Id}
""");
    }

    private static bool TryParseReason(string input, out string reason, out string error)
    {
        reason = "requested by agent";
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Input must be strict JSON object, for example: {\"reason\":\"user requested release verification\"}.";
                return false;
            }

            if (document.RootElement.TryGetProperty("reason", out JsonElement reasonElement))
            {
                if (reasonElement.ValueKind != JsonValueKind.String)
                {
                    error = "Input property reason must be a string.";
                    return false;
                }

                string? parsedReason = reasonElement.GetString();
                if (!string.IsNullOrWhiteSpace(parsedReason))
                {
                    reason = parsedReason.Trim();
                    if (reason.Length > 300)
                    {
                        reason = reason[..300];
                    }
                }
            }

            return true;
        }
        catch (JsonException)
        {
            error = "Input must be empty or strict JSON, for example: {\"reason\":\"user requested release verification\"}.";
            return false;
        }
    }
}

public static class ReleasePublishExecutor
{
    private const int TimeoutMilliseconds = 180_000;

    public static async Task<ReleasePublishResult> PublishAsync(PublishReleasePayload payload, CancellationToken cancellationToken)
    {
        string projectRoot = Path.GetFullPath(payload.ProjectRoot);
        if (!Directory.Exists(projectRoot))
        {
            return ReleasePublishResult.Fail($"Execution failed: project root does not exist: {projectRoot}");
        }

        string projectPath = Path.Combine(projectRoot, "TelegramMessagingTool", "TelegramMessagingTool.csproj");
        if (!File.Exists(projectPath))
        {
            return ReleasePublishResult.Fail($"Execution failed: expected project file does not exist: {projectPath}");
        }

        string releaseName = $"TelegramMessagingTool-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        string relativeReleasePath = Path.Combine("release", releaseName).Replace('\\', '/');
        string releasePath = Path.Combine(projectRoot, relativeReleasePath);
        Directory.CreateDirectory(releasePath);

        ProcessCommandResult publish = await RunDotnetAsync(
            projectRoot,
            ["publish", projectPath, "--configuration", "Release", "--output", releasePath, "--nologo"],
            cancellationToken);
        if (!publish.Success)
        {
            return ReleasePublishResult.Fail("Execution failed: dotnet publish failed. " + publish.RenderForMessage());
        }

        string latestReleaseFile = Path.Combine(projectRoot, ".latest-release");
        await File.WriteAllTextAsync(latestReleaseFile, relativeReleasePath, cancellationToken);
        return ReleasePublishResult.Ok($"Published release: {relativeReleasePath}");
    }

    private static async Task<ProcessCommandResult> RunDotnetAsync(string projectRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeoutMilliseconds);
            string output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            string error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return new ProcessCommandResult(process.ExitCode, output, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProcessCommandResult(-1, string.Empty, "dotnet publish timed out.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ProcessCommandResult(-1, string.Empty, ex.Message);
        }
    }
}

public sealed record PublishReleasePayload(string Action, string ProjectRoot, string Reason, DateTime RequestedAtUtc)
{
    public static PublishReleasePayload Empty { get; } = new(string.Empty, string.Empty, string.Empty, DateTime.MinValue);
}

public sealed record RestartLatestBotPayload(string Action, string ProjectRoot, string Reason, DateTime RequestedAtUtc)
{
    public static RestartLatestBotPayload Empty { get; } = new(string.Empty, string.Empty, string.Empty, DateTime.MinValue);
}

public interface ILatestReleaseRestarter
{
    Task<LatestReleaseRestartResult> RestartAsync(RestartLatestBotPayload payload, CancellationToken cancellationToken);
}

public sealed class SystemLatestReleaseRestarter : ILatestReleaseRestarter
{
    public Task<LatestReleaseRestartResult> RestartAsync(RestartLatestBotPayload payload, CancellationToken cancellationToken)
    {
        string projectRoot = Path.GetFullPath(payload.ProjectRoot);
        if (!Directory.Exists(projectRoot))
        {
            return Task.FromResult(LatestReleaseRestartResult.Fail($"Execution failed: project root does not exist: {projectRoot}"));
        }

        string latestReleaseFile = Path.Combine(projectRoot, ".latest-release");
        if (!File.Exists(latestReleaseFile))
        {
            return Task.FromResult(LatestReleaseRestartResult.Fail("Execution failed: .latest-release does not exist. Publish a release first."));
        }

        string latestRelease = File.ReadAllText(latestReleaseFile).Trim();
        if (string.IsNullOrWhiteSpace(latestRelease))
        {
            return Task.FromResult(LatestReleaseRestartResult.Fail("Execution failed: .latest-release is empty."));
        }

        string latestExe = Path.GetFullPath(Path.Combine(projectRoot, latestRelease, "TelegramMessagingTool.exe"));
        string rootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!latestExe.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) || !File.Exists(latestExe))
        {
            return Task.FromResult(LatestReleaseRestartResult.Fail($"Execution failed: latest release executable was not found under project root: {latestRelease}"));
        }

        string scriptPath = Path.Combine(projectRoot, "release", $"restart-latest-{DateTime.UtcNow:yyyyMMdd-HHmmss}.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        string script = $$"""
$ErrorActionPreference = 'Stop'
Start-Sleep -Seconds 2
$projectRoot = {{ToPowerShellString(projectRoot)}}
$latestExe = {{ToPowerShellString(latestExe)}}
$envNames = @('TELEGRAM_BOT_TOKEN','ADMIN_CHAT_ID','ALLOWED_CHAT_IDS','ALLOW_PUBLIC_ACCESS','OLLAMA_URL','OLLAMA_MODEL','OLLAMA_MODEL_CHAT','OLLAMA_MODEL_PLAN','OLLAMA_MODEL_DOC_QA','OLLAMA_MODEL_SUMMARY','OLLAMA_MODEL_TOOL_FINAL','OLLAMA_MODEL_IMAGE','OLLAMA_MODEL_VOICE','OLLAMA_EMBEDDING_URL','OLLAMA_EMBEDDING_MODEL','ENABLE_DOCUMENT_EMBEDDINGS','ENABLE_ONLINE_SEARCH','SEARCH_ROUTING_MODE','ENABLE_IMAGE_VISION','ENABLE_AUDIO_TRANSCRIPTION','ENABLE_SAFE_COMMAND_TOOLS','SAFE_COMMAND_PROJECT_ROOT','ENABLE_REPO_WRITE_TOOLS','ENABLE_PLUGINS','PLUGIN_DIRECTORY','ENABLE_GITHUB_TOOLS','ENABLE_GITHUB_WRITE_TOOLS','GITHUB_TOKEN','GITHUB_DEFAULT_OWNER','GITHUB_DEFAULT_REPO','GITHUB_ALLOWED_REPOS','TELEGRAM_DB_CONNECTION','APPLY_MIGRATIONS','LOG_MESSAGE_CONTENT','CONVERSATION_MAX_HISTORY')
foreach ($name in $envNames) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name, 'Process'))) {
        $userValue = [Environment]::GetEnvironmentVariable($name, 'User')
        if (-not [string]::IsNullOrWhiteSpace($userValue)) { [Environment]::SetEnvironmentVariable($name, $userValue, 'Process') }
    }
}
[Environment]::SetEnvironmentVariable('SAFE_COMMAND_PROJECT_ROOT', $projectRoot, 'Process')
[Environment]::SetEnvironmentVariable('ENABLE_REPO_WRITE_TOOLS', [Environment]::GetEnvironmentVariable('ENABLE_REPO_WRITE_TOOLS', 'Process'), 'Process')
Get-Process TelegramMessagingTool -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $PID } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $latestExe -WorkingDirectory $projectRoot -WindowStyle Hidden
""";
        File.WriteAllText(scriptPath, script);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        Process.Start(startInfo);
        return Task.FromResult(LatestReleaseRestartResult.Ok($"Scheduled restart from {latestRelease}. Script: {Path.GetFileName(scriptPath)}"));
    }

    private static string ToPowerShellString(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}

public sealed record LatestReleaseRestartResult(bool Success, string Message)
{
    public static LatestReleaseRestartResult Ok(string message) => new(true, message);

    public static LatestReleaseRestartResult Fail(string message) => new(false, message);
}

public sealed record ReleasePublishResult(bool Success, string Message)
{
    public static ReleasePublishResult Ok(string message) => new(true, message);

    public static ReleasePublishResult Fail(string message) => new(false, message);
}

public sealed class PublishReleaseRequestTool : ReleaseActionRequestTool
{
    public PublishReleaseRequestTool(PendingActionService pendingActionService, BotSettings settings, string projectRoot)
        : base(
            pendingActionService,
            settings,
            projectRoot,
            actionName: "publish_release",
            actionDescription: "Request approval to publish a new timestamped release.")
    {
    }

    public override string Name => "publish_release";

    public override string Description => "Approval-gated request only: creates a pending action to publish a timestamped release. Does not execute publish directly.";
}

public sealed class RestartLatestBotRequestTool : ReleaseActionRequestTool
{
    public RestartLatestBotRequestTool(PendingActionService pendingActionService, BotSettings settings, string projectRoot)
        : base(
            pendingActionService,
            settings,
            projectRoot,
            actionName: "restart_latest_bot",
            actionDescription: "Request approval to restart the bot from .latest-release.")
    {
    }

    public override string Name => "restart_latest_bot";

    public override string Description => "Approval-gated request only: creates a pending action to restart the latest bot release. Does not stop or start processes directly.";
}
