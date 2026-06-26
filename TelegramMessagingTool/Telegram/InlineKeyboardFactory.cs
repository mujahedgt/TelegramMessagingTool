using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramMessagingTool.Telegram;

public static class InlineKeyboardFactory
{
    public static InlineKeyboardMarkup ForPendingAction(int actionId)
    {
        return new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData("Approve", $"act:approve:{actionId}"),
                InlineKeyboardButton.WithCallbackData("Deny", $"act:deny:{actionId}"),
                InlineKeyboardButton.WithCallbackData("Details", $"act:details:{actionId}")
            ]
        ]);
    }
}
