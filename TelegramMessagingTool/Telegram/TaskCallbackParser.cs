namespace TelegramMessagingTool.Telegram;

public enum TaskCallbackVerb
{
    Open,
    Done,
    Cancel
}

public sealed record TaskCallback(TaskCallbackVerb Verb, int TaskId);

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
        if (parts.Length != 3)
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
            case "cancel":
                verb = TaskCallbackVerb.Cancel;
                return true;
            default:
                return false;
        }
    }
}
