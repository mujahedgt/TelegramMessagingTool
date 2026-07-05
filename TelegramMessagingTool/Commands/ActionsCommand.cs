using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ActionsCommand : IBotCommand
{
    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;

    public ActionsCommand(PendingActionService pendingActionService, BotSettings settings)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
    }

    public string Name => "/actions";

    public string Description => "List recent approval/action audit records.";

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

        int count = ParseCount(messageText);
        IReadOnlyList<PendingAction> actions = await _pendingActionService.ListRecentAsync(dbContext, user, count, cancellationToken);
        return new CommandResult(true, ActionHistoryFormatter.RenderRecent(actions));
    }

    private static int ParseCount(string messageText)
    {
        string rawCount = CommandParser.GetArguments(messageText, "/actions");
        if (int.TryParse(rawCount, out int count))
        {
            return Math.Clamp(count, 1, 50);
        }

        return 10;
    }
}
