namespace TelegramMessagingTool.Tools;

public sealed class BotStatusTool : IAgentTool
{
    private readonly BotSettings _settings;

    public BotStatusTool(BotSettings settings)
    {
        _settings = settings;
    }

    public string Name => "status";

    public string Description => "Returns the bot runtime configuration status.";

    public bool RequiresApproval => false;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        string output = $"""
Bot runtime status:
- Ollama URL: {_settings.OllamaUrl}
- Ollama model: {_settings.OllamaModel}
- Allowlist: {(_settings.AllowedChatIds.Count == 0 ? "disabled" : $"enabled ({_settings.AllowedChatIds.Count} chat IDs)")}
- Document embeddings: {(_settings.EnableDocumentEmbeddings ? "enabled" : "disabled")}
- Online search: {(_settings.EnableOnlineSearch ? "enabled" : "disabled")}
- Search routing: {_settings.SearchRoutingMode}
- Image vision: {(_settings.EnableImageVision ? "enabled" : "disabled")}
- Safe command tools: {(_settings.EnableSafeCommandTools ? "enabled" : "disabled")}
- Plugins: {(_settings.EnablePlugins ? "enabled" : "disabled")} ({_settings.PluginDirectory})
- Message content logging: {(_settings.LogMessageContent ? "enabled" : "disabled")}
- Apply migrations: {_settings.ApplyMigrations}
""";

        return Task.FromResult(ToolResult.Ok(output));
    }
}
