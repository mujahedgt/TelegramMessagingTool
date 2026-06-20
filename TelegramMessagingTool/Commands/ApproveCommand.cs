using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ApproveCommand : IBotCommand
{
    private readonly PendingActionService _pendingActionService;

    public ApproveCommand(PendingActionService pendingActionService)
    {
        _pendingActionService = pendingActionService;
    }

    public string Name => "/approve";
    public string Description => "Approve a pending action by ID.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/approve", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        if (!TryParseActionId(messageText, "/approve", out int actionId))
        {
            return new CommandResult(true, "Usage: /approve <pending-action-id>");
        }

        PendingActionDecision decision = await _pendingActionService.ApproveAsync(dbContext, user, actionId, cancellationToken);
        if (!decision.Success || decision.Action is null)
        {
            return new CommandResult(true, decision.Message);
        }

        return new CommandResult(true, $"Approved action #{decision.Action.Id}: {decision.Action.ToolName}\nNote: execution wiring for approved risky tools will be added in the next agent-tool phase.");
    }

    private static bool TryParseActionId(string messageText, string commandName, out int actionId)
    {
        string rawId = messageText[commandName.Length..].Trim();
        return int.TryParse(rawId, out actionId) && actionId > 0;
    }
}
