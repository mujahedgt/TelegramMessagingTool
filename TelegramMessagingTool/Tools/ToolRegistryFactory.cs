namespace TelegramMessagingTool.Tools;

public static class ToolRegistryFactory
{
    public static ToolRegistry Create(BotSettings settings, HttpClient searchClient)
    {
        var tools = new List<IAgentTool>
        {
            new DateTimeTool(),
            new CalculatorTool(),
            new BotStatusTool(settings)
        };

        if (settings.EnableOnlineSearch)
        {
            tools.Add(new OnlineSearchTool(searchClient));
        }

        return new ToolRegistry(tools);
    }
}
