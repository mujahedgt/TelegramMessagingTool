namespace TelegramMessagingTool.Telegram;

public enum PendingActionCallbackVerb
{
    Approve,
    Deny,
    Details
}

public sealed record PendingActionCallback(PendingActionCallbackVerb Verb, int ActionId);

public static class PendingActionCallbackParser
{
    private const string DomainPrefix = "act";

    public static bool TryParse(string? callbackData, out PendingActionCallback callback)
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

        if (!TryParseVerb(parts[1], out PendingActionCallbackVerb verb))
        {
            return false;
        }

        if (!int.TryParse(parts[2], out int actionId) || actionId <= 0)
        {
            return false;
        }

        callback = new PendingActionCallback(verb, actionId);
        return true;
    }

    private static bool TryParseVerb(string value, out PendingActionCallbackVerb verb)
    {
        verb = default;

        switch (value)
        {
            case "approve":
                verb = PendingActionCallbackVerb.Approve;
                return true;
            case "deny":
                verb = PendingActionCallbackVerb.Deny;
                return true;
            case "details":
                verb = PendingActionCallbackVerb.Details;
                return true;
            default:
                return false;
        }
    }
}
