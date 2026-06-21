using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class DenyCommand : IBotCommand
{
    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;

    public DenyCommand(PendingActionService pendingActionService, BotSettings settings)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
    }

    public string Name => "/deny";
    public string Description => "Deny a pending action by ID.";

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

        if (!TryParseActionId(messageText, "/deny", out int actionId))
        {
            return new CommandResult(true, "Usage: /deny <pending-action-id>");
        }

        PendingActionDecision decision = await _pendingActionService.DenyAsync(dbContext, user, actionId, cancellationToken);
        return new CommandResult(true, decision.Message);
    }

    private static bool TryParseActionId(string messageText, string commandName, out int actionId)
    {
        string rawId = CommandParser.GetArguments(messageText, commandName);
        return int.TryParse(rawId, out actionId) && actionId > 0;
    }
}
