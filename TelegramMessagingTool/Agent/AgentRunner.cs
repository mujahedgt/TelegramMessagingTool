using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.Agent;

public sealed class AgentRunner
{
    public const int DefaultMaxToolIterations = 3;

    private readonly IChatClient _chatClient;
    private readonly ToolRegistry _toolRegistry;
    private readonly ISearchRoutingClassifier _searchRoutingClassifier;
    private readonly int _maxToolIterations;

    public AgentRunner(
        IChatClient chatClient,
        ToolRegistry toolRegistry,
        int maxToolIterations = DefaultMaxToolIterations,
        ISearchRoutingClassifier? searchRoutingClassifier = null)
    {
        _chatClient = chatClient;
        _toolRegistry = toolRegistry;
        _searchRoutingClassifier = searchRoutingClassifier ?? new HeuristicSearchRoutingClassifier();
        _maxToolIterations = Math.Max(1, maxToolIterations);
    }

    public async Task<string> RunAsync(
        List<OllamaMessageDto> conversationContext,
        CancellationToken cancellationToken,
        TelegramDbContext? dbContext = null,
        ConnectedUser? user = null)
    {
        SearchRoutingDecision searchRoutingDecision = await _searchRoutingClassifier.ClassifyAsync(conversationContext, cancellationToken);
        if (searchRoutingDecision.ShouldSearch
            && _toolRegistry.TryGet("online_search", out IAgentTool? searchTool)
            && searchTool is not null)
        {
            ToolResult searchResult = await searchTool.ExecuteAsync(searchRoutingDecision.Query, cancellationToken);
            return await BuildOnlineSearchAnswerAsync(conversationContext, searchRoutingDecision.Query, searchResult, cancellationToken);
        }

        for (int step = 1; step <= _maxToolIterations; step++)
        {
            string assistantResponse = await _chatClient.AskAsync(conversationContext, cancellationToken, ModelTaskKind.Chat);
            ToolCallParseResult toolCall = ToolCallParser.Parse(assistantResponse);

            if (!toolCall.IsToolCall)
            {
                return assistantResponse;
            }

            if (string.IsNullOrWhiteSpace(toolCall.ToolName) || !_toolRegistry.TryGet(toolCall.ToolName, out IAgentTool? tool) || tool is null)
            {
                return $"I tried to call an unknown tool: {toolCall.ToolName}. Use /tools to see available tools.";
            }

            if (tool.RequiresApproval)
            {
                if (tool is IApprovalRequestTool approvalRequestTool && dbContext is not null && user is not null)
                {
                    ToolResult approvalRequestResult = await approvalRequestTool.CreatePendingActionAsync(
                        toolCall.Input,
                        dbContext,
                        user,
                        cancellationToken);
                    return approvalRequestResult.Success
                        ? approvalRequestResult.Output
                        : $"Could not create approval request for tool '{tool.Name}': {approvalRequestResult.Output}";
                }

                return $"Tool '{tool.Name}' requires approval. Use /pending, /approve <id>, and /deny <id> for risky actions once that tool is wired into the approval flow.";
            }

            ToolResult result = await tool.ExecuteAsync(toolCall.Input, cancellationToken);

            if (string.Equals(tool.Name, "online_search", StringComparison.OrdinalIgnoreCase))
            {
                return await BuildOnlineSearchAnswerAsync(conversationContext, toolCall.Input, result, cancellationToken);
            }

            conversationContext.Add(new OllamaMessageDto("assistant", assistantResponse));
            conversationContext.Add(new OllamaMessageDto(
                "user",
                BuildToolObservationPrompt(tool.Name, result, step, _maxToolIterations)));
        }

        string finalResponse = await _chatClient.AskAsync(conversationContext, cancellationToken, ModelTaskKind.Chat);
        if (ToolCallParser.Parse(finalResponse).IsToolCall)
        {
            return "I reached the safe tool-step limit before a final answer. Please narrow the request or ask me to continue with a smaller task.";
        }

        return finalResponse;
    }

    public static string BuildToolObservationPrompt(string toolName, ToolResult result, int step, int maxSteps)
    {
        bool canUseAnotherTool = step < maxSteps;
        string nextStepRule = canUseAnotherTool
            ? "If another safe tool is truly needed, you may reply with exactly one more strict tool_call JSON object. Otherwise, give the final answer now."
            : "You have reached the safe tool-step limit. Do not request another tool. Give the final answer now using only the observations above.";

        return $"""
Tool observation {step}/{maxSteps} from {toolName}:
Success: {result.Success}
Output:
{result.Output}

Next step rule:
{nextStepRule}

Final answer rules:
- Do not include or repeat raw tool_call JSON.
- Do not invent facts beyond tool observations and the conversation.
- If the tool failed, explain the failure clearly and suggest a safe next step.
""";
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
        string finalResponse = await _chatClient.AskAsync(conversationContext, cancellationToken, ModelTaskKind.ToolFinalAnswer);

        if (ToolCallParser.Parse(finalResponse).IsToolCall)
        {
            return "I searched online for: " + query + "\n\n" + result.Output;
        }

        return finalResponse;
    }

    public static bool TryBuildDirectSearchQuery(List<OllamaMessageDto> conversationContext, out string query)
    {
        return HeuristicSearchRoutingClassifier.TryBuildDirectSearchQuery(conversationContext, out query);
    }
}
