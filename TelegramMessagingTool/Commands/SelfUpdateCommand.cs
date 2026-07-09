using System.Text.Json;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class SelfUpdateCommand : IBotCommand
{
    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;

    public SelfUpdateCommand(PendingActionService pendingActionService, BotSettings settings)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
    }

    public string Name => "/selfupdate";

    public string Description => "Admin-only approval request to publish and restart the bot from the latest local source.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return new CommandResult(true, BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId));
        }

        string projectRoot = ResolveProjectRoot(_settings.SafeCommandProjectRoot);
        string projectFile = Path.Combine(projectRoot, "TelegramMessagingTool", "TelegramMessagingTool.csproj");
        if (!File.Exists(projectFile))
        {
            return new CommandResult(true, $"Self-update is not available because the project file was not found under SAFE_COMMAND_PROJECT_ROOT: {projectRoot}");
        }

        string reason = CommandParser.GetArguments(messageText, Name).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = "admin requested self-update";
        }

        if (reason.Length > 300)
        {
            reason = reason[..300];
        }

        string payloadJson = JsonSerializer.Serialize(new
        {
            action = "self_update_bot",
            project_root = projectRoot,
            reason,
            requested_at_utc = DateTime.UtcNow
        });

        PendingAction pendingAction = await _pendingActionService.CreateAsync(
            dbContext,
            user,
            toolName: "self_update_bot",
            description: $"Publish a timestamped release and restart the bot from .latest-release. Project root: {projectRoot}. Reason: {reason}",
            payloadJson: payloadJson,
            riskLevel: "high",
            ttl: TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken);

        string reply = $"""
Approval required for bot self-update.

Pending action #{pendingAction.Id}
Risk: high
Project root: {projectRoot}
Expires UTC: {pendingAction.ExpiresAt:yyyy-MM-dd HH:mm}

Plan after approval:
1. Publish a new timestamped release.
2. Update .latest-release.
3. Restart the bot from the latest release.

Approve: /approve {pendingAction.Id}
Deny: /deny {pendingAction.Id}
""";

        return new CommandResult(true, reply);
    }

    private static string ResolveProjectRoot(string configuredRoot)
    {
        string root = string.IsNullOrWhiteSpace(configuredRoot) ? Directory.GetCurrentDirectory() : configuredRoot;
        return Path.GetFullPath(root);
    }
}
