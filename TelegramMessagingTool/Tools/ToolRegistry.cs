using System.Text;

namespace TelegramMessagingTool.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IAgentTool> Tools => _tools.Values.OrderBy(x => x.Name).ToList();

    public bool TryGet(string name, out IAgentTool? tool)
    {
        return _tools.TryGetValue(name, out tool);
    }

    public string RenderToolList()
    {
        if (_tools.Count == 0)
        {
            return "No tools are registered.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("Available agent tools:");

        foreach (IAgentTool tool in Tools)
        {
            string approval = tool.RequiresApproval ? "requires approval" : "safe/no approval";
            builder.AppendLine($"- {tool.Name}: {tool.Description} ({approval})");
        }

        return builder.ToString().TrimEnd();
    }

    public string RenderToolInstructions()
    {
        if (_tools.Count == 0)
        {
            return "No tools are currently available.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("You can request one safe tool call at a time when needed. The app may allow another safe tool call after each tool observation, up to its configured step limit.");
        builder.AppendLine("To call a tool, reply only with strict JSON in this exact shape:");
        builder.AppendLine("{\"type\":\"tool_call\",\"tool\":\"tool_name\",\"input\":\"input text\"}");
        builder.AppendLine("Never mix tool-call JSON with normal text. The app will hide the JSON and run the tool for you.");
        if (TryGet("online_search", out _))
        {
            builder.AppendLine("Use online_search for current facts, prices, market values, news, products, cars, specs, or anything likely to need web data.");
            builder.AppendLine("For online_search, make the input a clean search query. Correct obvious spelling mistakes before searching only when the intended term is clear from the user's wording and context.");
            builder.AppendLine("If the user asks for prices, include words like price, used price, market value, or sale in the search query.");
        }
        else
        {
            builder.AppendLine("Online search is disabled by configuration. Do not claim access to current web data; answer from local context only or say that live web search is disabled.");
        }

        if (TryGet("run_dotnet_tests", out _))
        {
            builder.AppendLine("Use run_dotnet_tests only when the user asks to run or verify the helper tests. Its input must be strict JSON: {\"target\":\"helper-tests\"}.");
        }

        if (TryGet("publish_release", out _) || TryGet("restart_latest_bot", out _))
        {
            builder.AppendLine("publish_release and restart_latest_bot create high-risk pending approval requests only. They do not publish, stop, or restart anything directly.");
        }

        builder.AppendLine("Available tools:");

        foreach (IAgentTool tool in Tools)
        {
            builder.AppendLine($"- {tool.Name}: {tool.Description}");
        }

        builder.AppendLine("If no tool is needed, answer normally with plain text.");
        builder.AppendLine("After a tool runs, the application will send you the tool output. In that final answer, use only the provided tool output. Do not invent prices, specs, source names, or facts that are not present in the tool output.");
        return builder.ToString().TrimEnd();
    }
}
