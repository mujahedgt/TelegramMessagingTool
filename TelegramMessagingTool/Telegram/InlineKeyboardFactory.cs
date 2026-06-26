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

    public static InlineKeyboardMarkup ForTaskSummary(int taskId)
    {
        return new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("Open", $"task:open:{taskId}")]
        ]);
    }

    public static InlineKeyboardMarkup ForTaskDetails(int taskId)
    {
        return new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData("Done", $"task:done:{taskId}"),
                InlineKeyboardButton.WithCallbackData("Cancel", $"task:cancel:{taskId}")
            ]
        ]);
    }
}
