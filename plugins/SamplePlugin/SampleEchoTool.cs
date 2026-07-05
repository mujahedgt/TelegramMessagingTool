using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.SamplePlugin;

public sealed class SampleEchoTool : IAgentTool
{
    public string Name => "sample_echo";

    public string Description => "Sample trusted plugin tool that echoes its input.";

    public bool RequiresApproval => false;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        string value = string.IsNullOrWhiteSpace(input) ? "empty input" : input.Trim();
        return Task.FromResult(ToolResult.Ok($"sample_echo: {value}"));
    }
}
