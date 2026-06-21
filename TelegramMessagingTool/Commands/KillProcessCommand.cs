using System.Text.Json;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class KillProcessCommand : IBotCommand
{
    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;

    public KillProcessCommand(PendingActionService pendingActionService, BotSettings settings)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
    }

    public string Name => "/killprocess";

    public string Description => "Request approval to terminate a local process by PID.";

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

        string args = CommandParser.GetArguments(messageText, Name);
        if (!int.TryParse(args, out int processId) || processId <= 0)
        {
            return new CommandResult(true, "Usage: /killprocess <pid>\nThis creates a pending approval request only. It does not terminate anything immediately.");
        }

        string payloadJson = JsonSerializer.Serialize(new
        {
            pid = processId,
            requestedAtUtc = DateTime.UtcNow
        });

        PendingAction pendingAction = await _pendingActionService.CreateAsync(
            dbContext,
            user,
            toolName: "kill_process",
            description: $"Terminate local process PID {processId}. This is a risky local device-control action and will only execute after explicit approval wiring is added.",
            payloadJson: payloadJson,
            riskLevel: "high",
            ttl: TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken);

        string reply = $"""
Approval required before process termination.

Pending action #{pendingAction.Id}
Target PID: {processId}
Risk: high
Expires UTC: {pendingAction.ExpiresAt:yyyy-MM-dd HH:mm}

Approve: /approve {pendingAction.Id}
Deny: /deny {pendingAction.Id}

Safety note: this patch only creates the approval request. It does not kill the process yet.
""";

        return new CommandResult(true, reply);
    }
}
