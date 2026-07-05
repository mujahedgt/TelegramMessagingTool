using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public static class ActionHistoryFormatter
{
    public static string RenderRecent(IReadOnlyList<PendingAction> actions)
    {
        if (actions.Count == 0)
        {
            return "No recent actions were found for your account.";
        }

        return "Recent actions:\n\n" + string.Join("\n\n", actions.Select(RenderHistoryItem));
    }

    private static string RenderHistoryItem(PendingAction action)
    {
        string decided = action.DecidedAt is null
            ? "not decided"
            : action.DecidedAt.Value.ToString("yyyy-MM-dd HH:mm 'UTC'");
        string decisionNote = string.IsNullOrWhiteSpace(action.DecisionNote)
            ? "none"
            : Truncate(action.DecisionNote.ReplaceLineEndings(" ").Trim(), 220);

        return $"""
#{action.Id} [{action.Status}] {action.ToolName}
Risk: {Normalize(action.RiskLevel)}
Created UTC: {action.CreatedAt:yyyy-MM-dd HH:mm}
Decided UTC: {decided}
Decision: {decisionNote}
Details: /action {action.Id}
""".TrimEnd();
    }

    private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
