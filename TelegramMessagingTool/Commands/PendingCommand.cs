using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class PendingCommand : IBotCommand
{
    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;

    public PendingCommand(PendingActionService pendingActionService, BotSettings settings)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
    }

    public string Name => "/pending";
    public string Description => "List actions waiting for your approval.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/pending", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return new CommandResult(true, BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId));
        }

        IReadOnlyList<PendingAction> actions = await _pendingActionService.ListPendingAsync(dbContext, user, cancellationToken);
        if (actions.Count == 0)
        {
            return new CommandResult(true, "No pending actions are waiting for your approval.");
        }

        string reply = "Pending actions:\n\n" + string.Join("\n\n", actions.Select(RenderAction));
        return new CommandResult(true, reply);
    }

    private static string RenderAction(PendingAction action)
    {
        return $"#{action.Id} [{action.RiskLevel}] {action.ToolName}\n{action.Description}\nExpires UTC: {action.ExpiresAt:yyyy-MM-dd HH:mm}\nApprove: /approve {action.Id}\nDeny: /deny {action.Id}";
    }
}
