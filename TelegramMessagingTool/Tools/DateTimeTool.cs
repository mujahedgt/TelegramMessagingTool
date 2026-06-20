namespace TelegramMessagingTool.Tools;

public sealed class DateTimeTool : IAgentTool
{
    public string Name => "datetime";

    public string Description => "Returns the current UTC and local server date/time.";

    public bool RequiresApproval => false;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        DateTimeOffset localNow = DateTimeOffset.Now;

        string output = $"UTC: {utcNow:yyyy-MM-dd HH:mm:ss zzz}\nLocal server time: {localNow:yyyy-MM-dd HH:mm:ss zzz}";
        return Task.FromResult(ToolResult.Ok(output));
    }
}
