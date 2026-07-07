using TelegramMessagingTool;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Tools;

sealed record AgentBehaviorEvalResult(string Name, bool Passed, string Message);

sealed record AgentBehaviorEvalReport(IReadOnlyList<AgentBehaviorEvalResult> Results)
{
    public bool Passed => Results.All(x => x.Passed);

    public bool ContainsPassed(string name) => Results.Any(x => string.Equals(x.Name, name, StringComparison.Ordinal) && x.Passed);
}

static class AgentBehaviorEvalSuite
{
    public static async Task<AgentBehaviorEvalReport> RunAsync(CancellationToken cancellationToken)
    {
        List<AgentBehaviorEvalResult> results = [];
        results.Add(await EvalModelToolCallJsonIsExecuted(cancellationToken));
        results.Add(await EvalApprovalToolCreatesPendingAction(cancellationToken));
        results.Add(await EvalFailedToolResultIsExplainedSafely(cancellationToken));
        results.Add(await EvalSearchRoutingAvoidsFalsePositive(cancellationToken));
        return new AgentBehaviorEvalReport(results);
    }

    private static async Task<AgentBehaviorEvalResult> EvalModelToolCallJsonIsExecuted(CancellationToken cancellationToken)
    {
        var chatClient = new ScriptedChatClient([
            "{\"type\":\"tool_call\",\"tool\":\"calculator\",\"input\":\"2 + 2\"}",
            "The calculated answer is 4."
        ]);
        var runner = new AgentRunner(
            chatClient,
            new ToolRegistry([new CalculatorTool()]),
            searchRoutingClassifier: new OffSearchRoutingClassifier());

        string answer = await runner.RunAsync([new OllamaMessageDto("user", "calculate 2+2")], cancellationToken);
        bool passed = answer.Contains("4", StringComparison.OrdinalIgnoreCase) && chatClient.Calls == 2;
        return new AgentBehaviorEvalResult(
            "model_tool_call_json_is_executed",
            passed,
            passed ? "Model-emitted calculator tool_call was executed and finalized." : $"Unexpected answer/call count: {answer} / {chatClient.Calls}");
    }

    private static async Task<AgentBehaviorEvalResult> EvalApprovalToolCreatesPendingAction(CancellationToken cancellationToken)
    {
        var approvalTool = new ScriptedApprovalRequestTool();
        var chatClient = new ScriptedChatClient([
            "{\"type\":\"tool_call\",\"tool\":\"fake_approval_request\",\"input\":\"{\\\"reason\\\":\\\"eval\\\"}\"}"
        ]);
        var runner = new AgentRunner(
            chatClient,
            new ToolRegistry([approvalTool]),
            searchRoutingClassifier: new OffSearchRoutingClassifier());
        await using var dbContext = new TelegramDbContext();
        var user = new ConnectedUser { Id = 123, ChatId = 456, Name = "eval-user", CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow };

        string answer = await runner.RunAsync([new OllamaMessageDto("user", "request approval")], cancellationToken, dbContext, user);
        bool passed = approvalTool.CreateCallCount == 1 && answer.Contains("Pending action", StringComparison.OrdinalIgnoreCase);
        return new AgentBehaviorEvalResult(
            "approval_tool_creates_pending_action",
            passed,
            passed ? "Approval tool created a pending-action request through the runner." : $"Approval calls={approvalTool.CreateCallCount}, answer={answer}");
    }

    private static async Task<AgentBehaviorEvalResult> EvalFailedToolResultIsExplainedSafely(CancellationToken cancellationToken)
    {
        var chatClient = new ScriptedChatClient([
            "{\"type\":\"tool_call\",\"tool\":\"always_fail\",\"input\":\"bad input\"}",
            "The tool failed safely because the input was invalid. Try a smaller, valid request."
        ]);
        var runner = new AgentRunner(
            chatClient,
            new ToolRegistry([new AlwaysFailTool()]),
            searchRoutingClassifier: new OffSearchRoutingClassifier());

        string answer = await runner.RunAsync([new OllamaMessageDto("user", "use the failing tool")], cancellationToken);
        bool passed = answer.Contains("failed", StringComparison.OrdinalIgnoreCase)
            && answer.Contains("safely", StringComparison.OrdinalIgnoreCase)
            && !answer.Contains("tool_call", StringComparison.OrdinalIgnoreCase);
        return new AgentBehaviorEvalResult(
            "failed_tool_result_is_explained_safely",
            passed,
            passed ? "Failed tool observation produced a safe final explanation." : answer);
    }

    private static Task<AgentBehaviorEvalResult> EvalSearchRoutingAvoidsFalsePositive(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var classifier = new HeuristicSearchRoutingClassifier();
        SearchRoutingDecision decision = classifier.Classify([new OllamaMessageDto("user", "explain delegates in C#")]);
        bool passed = !decision.ShouldSearch;
        return Task.FromResult(new AgentBehaviorEvalResult(
            "search_routing_avoids_false_positive",
            passed,
            passed ? "Non-current programming explanation did not trigger direct online search." : $"Unexpected search query: {decision.Query}"));
    }
}

sealed class ScriptedApprovalRequestTool : IApprovalRequestTool
{
    public string Name => "fake_approval_request";

    public string Description => "Fake approval request eval tool.";

    public bool RequiresApproval => true;

    public int CreateCallCount { get; private set; }

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Fail("approval required"));
    }

    public Task<ToolResult> CreatePendingActionAsync(
        string input,
        TelegramDbContext dbContext,
        ConnectedUser user,
        CancellationToken cancellationToken)
    {
        CreateCallCount++;
        return Task.FromResult(ToolResult.Ok("Pending action #123 created for eval."));
    }
}

sealed class AlwaysFailTool : IAgentTool
{
    public string Name => "always_fail";

    public string Description => "Fake failing eval tool.";

    public bool RequiresApproval => false;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Fail("Eval failure: invalid input."));
    }
}
