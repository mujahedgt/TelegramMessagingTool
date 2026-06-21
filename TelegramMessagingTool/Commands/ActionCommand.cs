using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ActionCommand : IBotCommand
{
    private readonly PendingActionService _pendingActionService;

    public ActionCommand(PendingActionService pendingActionService)
    {
        _pendingActionService = pendingActionService;
    }

    public string Name => "/action";

    public string Description => "Show audit details for a pending or decided action.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/action", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        if (!TryParseActionId(messageText, out int actionId))
        {
            return new CommandResult(true, "Usage: /action <pending-action-id>");
        }

        PendingAction? action = await _pendingActionService.GetForUserAsync(dbContext, user, actionId, cancellationToken);
        if (action is null)
        {
            return new CommandResult(true, $"No action #{actionId} was found for your account.");
        }

        return new CommandResult(true, RenderActionDetails(action));
    }

    private static bool TryParseActionId(string messageText, out int actionId)
    {
        string rawId = messageText["/action".Length..].Trim();
        return int.TryParse(rawId, out actionId) && actionId > 0;
    }

    private static string RenderActionDetails(PendingAction action)
    {
        string decisionTime = action.DecidedAt is null
            ? "not decided"
            : action.DecidedAt.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        string decisionNote = string.IsNullOrWhiteSpace(action.DecisionNote)
            ? "none"
            : action.DecisionNote;

        return $"""
Action #{action.Id}

Type: {action.ToolName}
Risk: {action.RiskLevel}
Status: {action.Status}
Description: {action.Description}

Created UTC: {action.CreatedAt:yyyy-MM-dd HH:mm:ss}
Expires UTC: {action.ExpiresAt:yyyy-MM-dd HH:mm:ss}
Decided UTC: {decisionTime}

Decision note:
{decisionNote}

Payload:
{Truncate(action.PayloadJson, 1000)}
""";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "{}";
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
