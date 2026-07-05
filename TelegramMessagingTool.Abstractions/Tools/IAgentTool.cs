namespace TelegramMessagingTool.Tools;

public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    bool RequiresApproval { get; }

    ToolRiskLevel RiskLevel => RequiresApproval ? ToolRiskLevel.High : ToolRiskLevel.Low;

    bool IsReadOnly => !RequiresApproval;

    string SafetySummary => RequiresApproval
        ? "Requires approval before changing application, repository, or external state."
        : "Read-only or safe local tool that does not require approval.";

    Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken);
}
