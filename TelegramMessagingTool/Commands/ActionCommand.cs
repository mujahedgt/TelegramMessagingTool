using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ActionCommand : IBotCommand
{
    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;

    public ActionCommand(PendingActionService pendingActionService, BotSettings settings)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
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
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return new CommandResult(true, BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId));
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

        return new CommandResult(true, PendingActionPreviewFormatter.RenderDetails(action));
    }

    private static bool TryParseActionId(string messageText, out int actionId)
    {
        string rawId = CommandParser.GetArguments(messageText, "/action");
        return int.TryParse(rawId, out actionId) && actionId > 0;
    }
}
