using Telegram.Bot.Types.ReplyMarkups;
using TelegramMessagingTool.Models;

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

    public static InlineKeyboardMarkup ForTaskDetails(AgentTask task)
    {
        List<InlineKeyboardButton[]> rows = [];

        foreach (AgentTaskStep step in task.Steps.OrderBy(x => x.StepNumber).Where(x => !x.IsDone).Take(8))
        {
            rows.Add([InlineKeyboardButton.WithCallbackData($"Done step {step.StepNumber}", $"task:done-step:{task.Id}:{step.StepNumber}")]);
        }

        rows.Add([
            InlineKeyboardButton.WithCallbackData("Done all", $"task:done:{task.Id}"),
            InlineKeyboardButton.WithCallbackData("Cancel", $"task:cancel:{task.Id}")
        ]);

        return new InlineKeyboardMarkup(rows);
    }
}
