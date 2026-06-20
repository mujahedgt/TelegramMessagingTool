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
        string toolResultPrompt = $"""
Tool result from {tool.Name}:
Success: {result.Success}
Output:
{result.Output}

Now answer the user's original request clearly and briefly using the tool result.
Rules for the final answer:
- Do not include or repeat the raw tool_call JSON.
- If the search query was corrected or expanded, mention the correction briefly.
- For web search answers, summarize the useful facts and include the most relevant source links.
- Do not claim more than the tool result shows.
""";

        conversationContext.Add(new OllamaMessageDto("user", toolResultPrompt));

        return await _ollamaClient.AskAsync(conversationContext, cancellationToken);
    }
}
