using System.Text;

namespace TelegramMessagingTool.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolRegistration> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
        : this(tools.Select(ToolRegistration.FromBuiltIn))
    {
    }

    public ToolRegistry(IEnumerable<IAgentTool> builtInTools, IEnumerable<ToolRegistration> additionalTools)
        : this(builtInTools.Select(ToolRegistration.FromBuiltIn).Concat(additionalTools))
    {
    }

    public ToolRegistry(IEnumerable<ToolRegistration> tools)
    {
        _tools = tools.ToDictionary(x => x.Tool.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IAgentTool> Tools => _tools.Values.Select(x => x.Tool).OrderBy(x => x.Name).ToList();

    public bool TryGet(string name, out IAgentTool? tool)
    {
        if (_tools.TryGetValue(name, out ToolRegistration? registration))
        {
            tool = registration.Tool;
            return true;
        }

        tool = null;
        return false;
    }

    public string GetSource(string toolName)
    {
        return _tools.TryGetValue(toolName, out ToolRegistration? registration)
            ? registration.Source
            : string.Empty;
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
            string source = GetSource(tool.Name);
            ToolRegistration registration = _tools[tool.Name];
            string readOnly = registration.IsReadOnly ? "read-only" : "can change state";
            builder.AppendLine($"- {tool.Name}: {tool.Description} ({approval}; source: {source}; risk: {registration.RiskLevel.ToString().ToLowerInvariant()}; {readOnly}; safety: {registration.SafetySummary})");
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

        if (TryGet("dotnet_create_project", out _))
        {
            builder.AppendLine("Use dotnet_create_project when the user asks you to create/generate a C#/.NET console project. Do not merely paste project code when this tool is available; request the tool with a safe project name.");
            builder.AppendLine("For a console project that takes today's date and returns the nearest Friday, call dotnet_create_project with input {\"name\":\"NearestFridayApp\",\"template\":\"nearest_friday\"}.");
            builder.AppendLine("For a generic console project, call dotnet_create_project with input {\"name\":\"DemoApp\",\"template\":\"basic\"}.");
        }

        builder.AppendLine("Available tools:");

        foreach (IAgentTool tool in Tools)
        {
            ToolRegistration registration = _tools[tool.Name];
            builder.AppendLine($"- {tool.Name}: {tool.Description} (source: {GetSource(tool.Name)}, risk: {registration.RiskLevel.ToString().ToLowerInvariant()})");
        }

        builder.AppendLine("If no tool is needed, answer normally with plain text.");
        builder.AppendLine("After a tool runs, the application will send you the tool output. In that final answer, use only the provided tool output. Do not invent prices, specs, source names, or facts that are not present in the tool output.");
        return builder.ToString().TrimEnd();
    }
}

public sealed record ToolRegistration(
    IAgentTool Tool,
    string Source,
    ToolRiskLevel RiskLevel = ToolRiskLevel.Low,
    bool IsReadOnly = true,
    string SafetySummary = "Built-in tool managed by the application.")
{
    public const string BuiltInSource = "built-in";

    public static ToolRegistration FromBuiltIn(IAgentTool tool)
    {
        return new ToolRegistration(
            tool,
            BuiltInSource,
            tool.RiskLevel,
            tool.IsReadOnly,
            tool.SafetySummary);
    }
}
