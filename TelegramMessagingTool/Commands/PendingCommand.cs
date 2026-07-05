using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Telegram;

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
        if (!CommandParser.Matches(messageText, Name))
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

        string reply = "Pending actions:\n\n" + string.Join("\n\n", actions.Select(PendingActionPreviewFormatter.RenderListItem));
        return new CommandResult(true, reply, InlineKeyboardFactory.ForPendingAction(actions[0].Id));
    }
}
