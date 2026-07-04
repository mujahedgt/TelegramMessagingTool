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
