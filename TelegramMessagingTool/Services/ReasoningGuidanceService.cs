namespace TelegramMessagingTool.Services;

public static class ReasoningGuidanceService
{
    private static readonly string[] ComplexTaskSignals =
    [
        "plan",
        "debug",
        "fix",
        "design",
        "architecture",
        "compare",
        "trade-off",
        "tradeoff",
        "migrate",
        "migration",
        "implement",
        "refactor",
        "analyze",
        "evaluate",
        "why",
        "root cause",
        "step by step",
        "roadmap"
    ];

    public static string BuildGuidance(string latestUserMessage)
    {
        if (!ShouldAddReasoningGuidance(latestUserMessage))
        {
            return string.Empty;
        }

        return """
Reasoning guidance:
Use a private reasoning checklist before answering:
1. Identify the user's actual goal and any missing assumptions.
2. Decide whether current facts, files, tools, or command evidence are required before answering.
3. Break complex work into small safe steps and mention verification when relevant.
4. For comparisons, name the decision criteria and trade-offs.
5. For debugging, separate symptom, likely cause, exact check, exact fix, and verification.

Final answer discipline:
- Give the concise result, plan, or next action; do not dump hidden reasoning.
- Do not reveal private chain-of-thought.
- If evidence is missing, state the assumption or ask only for the specific missing input.
""";
    }

    public static bool ShouldAddReasoningGuidance(string latestUserMessage)
    {
        if (string.IsNullOrWhiteSpace(latestUserMessage))
        {
            return false;
        }

        string trimmed = latestUserMessage.Trim();
        if (trimmed.Length >= 120)
        {
            return true;
        }

        int questionMarks = trimmed.Count(static c => c == '?');
        if (questionMarks >= 2)
        {
            return true;
        }

        return ComplexTaskSignals.Any(signal => trimmed.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }
}
