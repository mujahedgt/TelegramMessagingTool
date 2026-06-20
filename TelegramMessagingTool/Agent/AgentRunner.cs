using System.Text.RegularExpressions;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.Agent;

public sealed class AgentRunner
{
    private readonly OllamaChatClient _ollamaClient;
    private readonly ToolRegistry _toolRegistry;

    public AgentRunner(OllamaChatClient ollamaClient, ToolRegistry toolRegistry)
    {
        _ollamaClient = ollamaClient;
        _toolRegistry = toolRegistry;
    }

    public async Task<string> RunAsync(List<OllamaMessageDto> conversationContext, CancellationToken cancellationToken)
    {
        if (TryBuildDirectSearchQuery(conversationContext, out string directSearchQuery)
            && _toolRegistry.TryGet("online_search", out IAgentTool? searchTool)
            && searchTool is not null)
        {
            ToolResult searchResult = await searchTool.ExecuteAsync(directSearchQuery, cancellationToken);
            return await BuildOnlineSearchAnswerAsync(conversationContext, directSearchQuery, searchResult, cancellationToken);
        }

        string firstResponse = await _ollamaClient.AskAsync(conversationContext, cancellationToken);
        ToolCallParseResult toolCall = ToolCallParser.Parse(firstResponse);

        if (!toolCall.IsToolCall)
        {
            return firstResponse;
        }

        if (string.IsNullOrWhiteSpace(toolCall.ToolName) || !_toolRegistry.TryGet(toolCall.ToolName, out IAgentTool? tool) || tool is null)
        {
            return $"I tried to call an unknown tool: {toolCall.ToolName}. Use /tools to see available tools.";
        }

        if (tool.RequiresApproval)
        {
            return $"Tool '{tool.Name}' requires approval, but approval flow is not implemented yet.";
        }

        ToolResult result = await tool.ExecuteAsync(toolCall.Input, cancellationToken);

        if (string.Equals(tool.Name, "online_search", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildOnlineSearchAnswerAsync(conversationContext, toolCall.Input, result, cancellationToken);
        }

        string toolResultPrompt = $"""
Tool result from {tool.Name}:
Success: {result.Success}
Output:
{result.Output}

Now answer the user's original request clearly and briefly using ONLY the tool result above.
Rules for the final answer:
- Do not include or repeat the raw tool_call JSON.
- Do not use memory, general knowledge, or invented examples to fill gaps.
- If the search query was corrected or expanded, mention the correction briefly.
- For web search answers, cite only URLs that appear in the tool output.
- If the user asked for prices but the tool output does not show actual prices, say that the search found relevant pages but did not expose exact prices in the returned snippets; then give the likely next source links to check.
- Do not name sources, prices, specs, trims, or years unless they appear in the tool output.
- Do not claim more than the tool result shows.
""";

        conversationContext.Add(new OllamaMessageDto("user", toolResultPrompt));

        return await _ollamaClient.AskAsync(conversationContext, cancellationToken);
    }

    private async Task<string> BuildOnlineSearchAnswerAsync(List<OllamaMessageDto> conversationContext, string query, ToolResult result, CancellationToken cancellationToken)
    {
        if (!result.Success)
        {
            return "I tried to search online for: " + query + "\n\nSearch failed:\n" + result.Output;
        }

        string finalSearchPrompt = $"""
Online search was executed for the user's request.

Search query used:
{query}

Tool output, including search result links and any page extracts that could be read:
{result.Output}

Now answer the user's original question in a helpful way using ONLY the tool output above.
Rules:
- If page extracts are available, use them first, not just the search result titles.
- If no page extracts are available, say that clearly and summarize only what the search result titles/snippets show.
- Cite the source URLs you used.
- Do not invent model names, years, prices, specs, or claims that are not present in the tool output.
- Keep the answer concise.
""";

        conversationContext.Add(new OllamaMessageDto("user", finalSearchPrompt));
        return await _ollamaClient.AskAsync(conversationContext, cancellationToken);
    }

    public static bool TryBuildDirectSearchQuery(List<OllamaMessageDto> conversationContext, out string query)
    {
        query = string.Empty;
        string? userText = conversationContext
            .LastOrDefault(x => string.Equals(x.role, "user", StringComparison.OrdinalIgnoreCase))
            ?.content;

        if (string.IsNullOrWhiteSpace(userText))
        {
            return false;
        }

        bool asksForSearch = ContainsAny(userText, ["search", "searsh", "google", "look up", "find online", "online", "web"]);
        bool asksForCurrentInfo = ContainsAny(userText, ["latest", "newest", "recent", "current", "currently", "today", "this year", "new model", "new car", "just released", "released", "upcoming", "2025", "2026"]);
        bool asksForCurrentMarketData = ContainsAny(userText, ["price", "prices", "market value", "current value", "for sale"]);
        if (!asksForSearch && !asksForCurrentInfo && !asksForCurrentMarketData)
        {
            return false;
        }

        Match quoted = Regex.Match(userText, "[\"“”'](?<query>[^\"“”']{2,120})[\"“”']");
        query = quoted.Success ? quoted.Groups["query"].Value.Trim() : userText.Trim();

        if (asksForCurrentMarketData && !ContainsAny(query, ["price", "prices", "market", "value", "sale", "spec", "specs"]))
        {
            query += " price specs";
        }

        if (asksForCurrentInfo && !ContainsAny(query, ["official", "2025", "2026", "latest model", "new model"]))
        {
            query += " 2026 official latest model";
        }

        return !string.IsNullOrWhiteSpace(query);
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
