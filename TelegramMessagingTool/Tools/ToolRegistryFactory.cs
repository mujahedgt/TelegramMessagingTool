using TelegramMessagingTool.Tools.CommandExecution;

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

        if (settings.EnableSafeCommandTools)
        {
            tools.Add(new GitStatusTool(settings.SafeCommandProjectRoot));
            tools.Add(new GitDiffTool(settings.SafeCommandProjectRoot));
            tools.Add(new GitLogRecentTool(settings.SafeCommandProjectRoot));
            tools.Add(new RunDotnetTestsTool(settings.SafeCommandProjectRoot));
        }

        return new ToolRegistry(tools);
    }
}
