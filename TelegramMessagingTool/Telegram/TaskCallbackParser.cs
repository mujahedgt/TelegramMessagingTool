namespace TelegramMessagingTool.Telegram;

public enum TaskCallbackVerb
{
    Open,
    Done,
    DoneStep,
    Cancel
}

public sealed record TaskCallback(TaskCallbackVerb Verb, int TaskId, int? StepNumber = null);

public static class TaskCallbackParser
{
    private const string DomainPrefix = "task";

    public static bool TryParse(string? callbackData, out TaskCallback callback)
    {
        callback = default!;

        if (string.IsNullOrWhiteSpace(callbackData))
        {
            return false;
        }

        string[] parts = callbackData.Split(':', StringSplitOptions.None);
        if (parts.Length is not (3 or 4))
        {
            return false;
        }

        if (!string.Equals(parts[0], DomainPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryParseVerb(parts[1], out TaskCallbackVerb verb))
        {
            return false;
        }

        if (!int.TryParse(parts[2], out int taskId) || taskId <= 0)
        {
            return false;
        }

        if (verb == TaskCallbackVerb.DoneStep)
        {
            if (parts.Length != 4 || !int.TryParse(parts[3], out int stepNumber) || stepNumber <= 0)
            {
                return false;
            }

            callback = new TaskCallback(verb, taskId, stepNumber);
            return true;
        }

        if (parts.Length != 3)
        {
            return false;
        }

        callback = new TaskCallback(verb, taskId);
        return true;
    }

    private static bool TryParseVerb(string value, out TaskCallbackVerb verb)
    {
        verb = default;

        switch (value)
        {
            case "open":
                verb = TaskCallbackVerb.Open;
                return true;
            case "done":
                verb = TaskCallbackVerb.Done;
                return true;
            case "done-step":
                verb = TaskCallbackVerb.DoneStep;
                return true;
            case "cancel":
                verb = TaskCallbackVerb.Cancel;
                return true;
            default:
                return false;
        }
    }
}
