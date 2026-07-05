namespace TelegramMessagingTool.Tools;

public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    bool RequiresApproval { get; }

    Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken);
}
