using Microsoft.EntityFrameworkCore;
using System.Net.Sockets;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramMessagingTool;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Commands;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Telegram;
using TelegramMessagingTool.Tools;
using TelegramMessagingTool.Tools.CommandExecution;

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"{name}: expected '{expected}', actual '{actual}'");
    }
}

static void AssertTrue(bool condition, string name)
{
    if (!condition)
    {
        throw new Exception($"{name}: expected true");
    }
}

static void AssertFalse(bool condition, string name)
{
    if (condition)
    {
        throw new Exception($"{name}: expected false");
    }
}

// RED tests for upgrade helpers.
List<string> chunks = TelegramMessageFormatter.SplitForTelegram(new string('x', 9000), 4096).ToList();
AssertEqual(3, chunks.Count, "SplitForTelegram creates 3 chunks for 9000 chars");
AssertTrue(chunks.All(x => x.Length <= 4096), "SplitForTelegram respects Telegram limit");
AssertEqual(new string('x', 9000), string.Concat(chunks), "SplitForTelegram preserves content");

string redacted = TelegramMessageFormatter.RedactForLogs("hello\nworld\rtest");
AssertFalse(redacted.Contains('\n'), "RedactForLogs removes new lines");
AssertFalse(redacted.Contains('\r'), "RedactForLogs removes carriage returns");

var transientTelegramException = new HttpRequestException(
    "An error occurred while sending the request.",
    new IOException(
        "Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.",
        new SocketException(10054)));
AssertTrue(TelegramReceiverErrorClassifier.IsTransientNetworkError(transientTelegramException), "TelegramReceiverErrorClassifier detects socket reset as transient");
AssertTrue(TelegramReceiverErrorClassifier.Summarize(transientTelegramException).Contains("Long polling will continue automatically"), "TelegramReceiverErrorClassifier summarizes transient network errors without stack spam");
AssertFalse(TelegramReceiverErrorClassifier.IsTransientNetworkError(new InvalidOperationException("bad config")), "TelegramReceiverErrorClassifier does not hide non-network errors");

var allowlist = BotAccessPolicy.ParseAllowedChatIds("123, 456, invalid,789");
var emptyAllowlist = new HashSet<long>();
AssertTrue(BotAccessPolicy.IsAllowed(123, allowlist, adminChatId: 777, allowPublicAccess: false), "allowlist includes 123");
AssertTrue(BotAccessPolicy.IsAllowed(456, allowlist, adminChatId: 777, allowPublicAccess: false), "allowlist includes 456");
AssertFalse(BotAccessPolicy.IsAllowed(999, allowlist, adminChatId: 777, allowPublicAccess: false), "allowlist blocks unknown chat when configured");
AssertFalse(BotAccessPolicy.IsAllowed(999, emptyAllowlist, adminChatId: 0, allowPublicAccess: false), "empty allowlist fails closed when public access is not explicitly enabled");
AssertTrue(BotAccessPolicy.IsAllowed(999, emptyAllowlist, adminChatId: 0, allowPublicAccess: true), "public override allows unknown chat when explicitly enabled");
AssertTrue(BotAccessPolicy.IsAllowed(777, emptyAllowlist, adminChatId: 777, allowPublicAccess: false), "admin chat is allowed even without allowlist");
AssertTrue(BotAccessPolicy.AccessDeniedMessage(false, emptyAllowlist, 0).Contains("ALLOW_PUBLIC_ACCESS"), "AccessDeniedMessage explains fully locked configuration");
AssertTrue(BotAccessPolicy.AccessDeniedMessage(false, allowlist, 0).Contains("administrator", StringComparison.OrdinalIgnoreCase), "AccessDeniedMessage gives normal allowlist denial text");
AssertEqual("allowlist", BotAccessPolicy.DescribeAccessMode(allowlist, adminChatId: 0, allowPublicAccess: false), "DescribeAccessMode reports allowlist mode");
AssertEqual("admin-only", BotAccessPolicy.DescribeAccessMode(emptyAllowlist, adminChatId: 777, allowPublicAccess: false), "DescribeAccessMode reports admin-only mode");
AssertEqual("public override", BotAccessPolicy.DescribeAccessMode(emptyAllowlist, adminChatId: 0, allowPublicAccess: true), "DescribeAccessMode reports public override mode");
AssertEqual("locked", BotAccessPolicy.DescribeAccessMode(emptyAllowlist, adminChatId: 0, allowPublicAccess: false), "DescribeAccessMode reports locked mode");

AssertEqual(8, BotConfiguration.ParseClampedInt(null, defaultValue: 8, minValue: 1, maxValue: 50), "ParseClampedInt returns default for missing value");
AssertEqual(12, BotConfiguration.ParseClampedInt("12", defaultValue: 8, minValue: 1, maxValue: 50), "ParseClampedInt parses valid value");
AssertEqual(1, BotConfiguration.ParseClampedInt("0", defaultValue: 8, minValue: 1, maxValue: 50), "ParseClampedInt clamps below minimum");
AssertEqual(50, BotConfiguration.ParseClampedInt("500", defaultValue: 8, minValue: 1, maxValue: 50), "ParseClampedInt clamps above maximum");
AssertEqual(8, BotConfiguration.ParseClampedInt("not-a-number", defaultValue: 8, minValue: 1, maxValue: 50), "ParseClampedInt returns default for invalid value");
string? previousConversationMaxHistory = Environment.GetEnvironmentVariable("CONVERSATION_MAX_HISTORY");
string? previousSearchRoutingMode = Environment.GetEnvironmentVariable("SEARCH_ROUTING_MODE");
string? previousEnableSafeCommandTools = Environment.GetEnvironmentVariable("ENABLE_SAFE_COMMAND_TOOLS");
string? previousSafeCommandProjectRoot = Environment.GetEnvironmentVariable("SAFE_COMMAND_PROJECT_ROOT");
try
{
    Environment.SetEnvironmentVariable("CONVERSATION_MAX_HISTORY", "13");
    AssertEqual(13, BotConfiguration.LoadFromEnvironment().ConversationMaxHistory, "LoadFromEnvironment reads CONVERSATION_MAX_HISTORY");

    Environment.SetEnvironmentVariable("CONVERSATION_MAX_HISTORY", "500");
    AssertEqual(50, BotConfiguration.LoadFromEnvironment().ConversationMaxHistory, "LoadFromEnvironment clamps high CONVERSATION_MAX_HISTORY");

    Environment.SetEnvironmentVariable("SEARCH_ROUTING_MODE", null);
    AssertEqual("heuristic", BotConfiguration.LoadFromEnvironment().SearchRoutingMode, "LoadFromEnvironment defaults SEARCH_ROUTING_MODE to heuristic");

    Environment.SetEnvironmentVariable("SEARCH_ROUTING_MODE", "off");
    AssertEqual("off", BotConfiguration.LoadFromEnvironment().SearchRoutingMode, "LoadFromEnvironment reads SEARCH_ROUTING_MODE=off");

    Environment.SetEnvironmentVariable("SEARCH_ROUTING_MODE", "llm");
    AssertEqual("llm", BotConfiguration.LoadFromEnvironment().SearchRoutingMode, "LoadFromEnvironment reads SEARCH_ROUTING_MODE=llm");

    Environment.SetEnvironmentVariable("SEARCH_ROUTING_MODE", "unknown");
    AssertEqual("heuristic", BotConfiguration.LoadFromEnvironment().SearchRoutingMode, "LoadFromEnvironment falls back to heuristic for invalid SEARCH_ROUTING_MODE");

    Environment.SetEnvironmentVariable("ENABLE_SAFE_COMMAND_TOOLS", null);
    AssertFalse(BotConfiguration.LoadFromEnvironment().EnableSafeCommandTools, "LoadFromEnvironment defaults safe command tools to disabled");

    Environment.SetEnvironmentVariable("ENABLE_SAFE_COMMAND_TOOLS", "true");
    Environment.SetEnvironmentVariable("SAFE_COMMAND_PROJECT_ROOT", Directory.GetCurrentDirectory());
    BotSettings safeCommandEnvironmentSettings = BotConfiguration.LoadFromEnvironment();
    AssertTrue(safeCommandEnvironmentSettings.EnableSafeCommandTools, "LoadFromEnvironment parses ENABLE_SAFE_COMMAND_TOOLS truthy values");
    AssertEqual(Path.GetFullPath(Directory.GetCurrentDirectory()), safeCommandEnvironmentSettings.SafeCommandProjectRoot, "LoadFromEnvironment reads SAFE_COMMAND_PROJECT_ROOT as a full path");
}
finally
{
    Environment.SetEnvironmentVariable("CONVERSATION_MAX_HISTORY", previousConversationMaxHistory);
    Environment.SetEnvironmentVariable("SEARCH_ROUTING_MODE", previousSearchRoutingMode);
    Environment.SetEnvironmentVariable("ENABLE_SAFE_COMMAND_TOOLS", previousEnableSafeCommandTools);
    Environment.SetEnvironmentVariable("SAFE_COMMAND_PROJECT_ROOT", previousSafeCommandProjectRoot);
}

AssertTrue(CommandParser.TryParse("/status", out ParsedCommand parsedStatus), "CommandParser parses bare command");
AssertEqual("/status", parsedStatus.Command, "CommandParser normalizes bare command token");
AssertEqual(string.Empty, parsedStatus.Arguments, "CommandParser returns empty arguments for bare command");
AssertTrue(CommandParser.TryParse("/status@red_eye_ghost_bot detailed", out ParsedCommand parsedMentionCommand), "CommandParser parses command addressed to bot username");
AssertEqual("/status", parsedMentionCommand.Command, "CommandParser strips bot username suffix");
AssertEqual("red_eye_ghost_bot", parsedMentionCommand.BotUsername, "CommandParser captures bot username suffix");
AssertEqual("detailed", parsedMentionCommand.Arguments, "CommandParser extracts arguments after mention command");
AssertFalse(CommandParser.Matches("/statusx", "/status"), "CommandParser does not match command prefixes");
AssertFalse(CommandParser.Matches("/status-extra", "/status"), "CommandParser does not match command names with extra suffixes");
AssertTrue(CommandParser.Matches("/status@red_eye_ghost_bot", "/status"), "CommandParser matches bot-addressed commands");
AssertEqual("hello world", CommandParser.GetArguments("/remember@red_eye_ghost_bot hello world", "/remember"), "CommandParser extracts arguments from bot-addressed command");

string validOllamaJson = """
{
  "message": {
    "role": "assistant",
    "content": "Hello from Ollama"
  }
}
""";
AssertEqual("Hello from Ollama", OllamaChatClient.ParseAssistantContent(validOllamaJson), "ParseAssistantContent reads assistant content");
AssertEqual("Invalid response received from Ollama.", OllamaChatClient.ParseAssistantContent("not json"), "ParseAssistantContent handles invalid JSON");

string promptWithMemory = ConversationService.BuildSystemPrompt([
    new Memory { Content = "User is learning C#." }
]);
AssertTrue(promptWithMemory.Contains("Known memories about this user:"), "BuildSystemPrompt includes memory heading");
AssertTrue(promptWithMemory.Contains("User is learning C#."), "BuildSystemPrompt includes memory content");
AssertTrue(promptWithMemory.Contains("Available Telegram commands"), "BuildSystemPrompt documents commands");
AssertTrue(promptWithMemory.Contains("live web search is disabled"), "BuildSystemPrompt tells model not to guess when search is unavailable");
AssertFalse(promptWithMemory.Contains("request the `online_search` tool"), "BuildSystemPrompt does not advertise online_search without tool instructions");
string promptWithSearchInstructions = ConversationService.BuildSystemPrompt([], "Available tools:\n- online_search: Search web");
AssertTrue(promptWithSearchInstructions.Contains("online_search"), "BuildSystemPrompt includes online_search only from active tool instructions");
AssertFalse(promptWithMemory.Contains("unless a future tool system actually provides"), "BuildSystemPrompt does not mention unavailable future tool-calling");

ToolCallParseResult noToolCall = ToolCallParser.Parse("Normal assistant response");
AssertFalse(noToolCall.IsToolCall, "ToolCallParser ignores normal text");

ToolCallParseResult parsedToolCall = ToolCallParser.Parse("{\"type\":\"tool_call\",\"tool\":\"calculator\",\"input\":\"25*19\"}");
AssertTrue(parsedToolCall.IsToolCall, "ToolCallParser accepts strict tool call JSON");
AssertEqual("calculator", parsedToolCall.ToolName, "ToolCallParser extracts tool name");
AssertEqual("25*19", parsedToolCall.Input, "ToolCallParser extracts tool input");
ToolCallParseResult embeddedToolCall = ToolCallParser.Parse("I will search now:\n{\"type\":\"tool_call\",\"tool\":\"online_search\",\"input\":\"Mitsubishi Lancer 1992 price specs\"}");
AssertTrue(embeddedToolCall.IsToolCall, "ToolCallParser extracts embedded tool call JSON from chatty model output");
AssertEqual("online_search", embeddedToolCall.ToolName, "ToolCallParser extracts embedded tool name");

var heuristicSearchRoutingClassifier = new HeuristicSearchRoutingClassifier();
SearchRoutingDecision newestMitsubishiDecision = heuristicSearchRoutingClassifier.Classify([new OllamaMessageDto("user", "what is the newest car from mitsubishi")]);
AssertTrue(newestMitsubishiDecision.ShouldSearch, "HeuristicSearchRoutingClassifier directly searches newest/current factual questions");
AssertTrue(newestMitsubishiDecision.Query.Contains("Mitsubishi", StringComparison.OrdinalIgnoreCase), "HeuristicSearchRoutingClassifier keeps the requested brand in direct search query");
AssertTrue(newestMitsubishiDecision.Query.Contains("official", StringComparison.OrdinalIgnoreCase), "HeuristicSearchRoutingClassifier expands newest/current direct search query with official/latest context");
AssertTrue(
    AgentRunner.TryBuildDirectSearchQuery([new OllamaMessageDto("user", "what is the newest car from mitsubishi")], out string newestMitsubishiQuery),
    "AgentRunner compatibility helper delegates direct search classification");
AssertEqual(newestMitsubishiDecision.Query, newestMitsubishiQuery, "AgentRunner compatibility helper preserves heuristic search query behavior");
AssertFalse(
    heuristicSearchRoutingClassifier.Classify([new OllamaMessageDto("user", "explain delegates in C#")]).ShouldSearch,
    "HeuristicSearchRoutingClassifier skips non-current local/explanation questions");
ISearchRoutingClassifier offSearchRoutingClassifier = SearchRoutingClassifierFactory.Create("off");
SearchRoutingDecision offSearchDecision = await offSearchRoutingClassifier.ClassifyAsync(
    [new OllamaMessageDto("user", "what is the newest car from mitsubishi")],
    CancellationToken.None);
AssertFalse(offSearchDecision.ShouldSearch, "SearchRoutingClassifierFactory creates an off classifier that disables direct online search");
AssertTrue(SearchRoutingClassifierFactory.Create("heuristic") is HeuristicSearchRoutingClassifier, "SearchRoutingClassifierFactory creates heuristic classifier");
AssertTrue(SearchRoutingClassifierFactory.Create("invalid") is HeuristicSearchRoutingClassifier, "SearchRoutingClassifierFactory falls back to heuristic classifier");
var llmRoutingClient = new ScriptedChatClient([
    "{\"should_search\":true,\"query\":\"latest .NET version\",\"reason\":\"needs current external facts\",\"confidence\":0.92}"
]);
ISearchRoutingClassifier llmSearchRoutingClassifier = SearchRoutingClassifierFactory.Create("llm", llmRoutingClient);
AssertTrue(llmSearchRoutingClassifier is LlmSearchRoutingClassifier, "SearchRoutingClassifierFactory creates LLM classifier when chat client is provided");
SearchRoutingDecision llmSearchDecision = await llmSearchRoutingClassifier.ClassifyAsync(
    [new OllamaMessageDto("user", "what is the latest .NET version")],
    CancellationToken.None);
AssertTrue(llmSearchDecision.ShouldSearch, "LlmSearchRoutingClassifier accepts should_search=true JSON");
AssertEqual("latest .NET version", llmSearchDecision.Query, "LlmSearchRoutingClassifier parses query");
AssertEqual("needs current external facts", llmSearchDecision.Reason, "LlmSearchRoutingClassifier parses reason");
AssertEqual(ModelTaskKind.Chat, llmRoutingClient.ModelTaskKinds.Single(), "LlmSearchRoutingClassifier uses chat route for classification");
var invalidLlmRoutingClient = new ScriptedChatClient(["not json"]);
SearchRoutingDecision invalidLlmSearchDecision = await new LlmSearchRoutingClassifier(invalidLlmRoutingClient).ClassifyAsync(
    [new OllamaMessageDto("user", "latest phone price")],
    CancellationToken.None);
AssertFalse(invalidLlmSearchDecision.ShouldSearch, "LlmSearchRoutingClassifier falls back to no-search on invalid JSON");

var calculator = new CalculatorTool();
ToolResult calculation = await calculator.ExecuteAsync("25 * 19", CancellationToken.None);
AssertTrue(calculation.Success, "CalculatorTool accepts safe math");
AssertTrue(calculation.Output.Contains("475"), "CalculatorTool calculates multiplication");
ToolResult rejectedCalculation = await calculator.ExecuteAsync("System.IO.File.Delete('x')", CancellationToken.None);
AssertFalse(rejectedCalculation.Success, "CalculatorTool rejects non-math input");

var dateTimeTool = new DateTimeTool();
ToolResult dateTimeResult = await dateTimeTool.ExecuteAsync("", CancellationToken.None);
AssertTrue(dateTimeResult.Success, "DateTimeTool succeeds");
AssertTrue(dateTimeResult.Output.Contains("UTC"), "DateTimeTool reports UTC time");

string observationPrompt = AgentRunner.BuildToolObservationPrompt("calculator", ToolResult.Ok("475"), 1, 3);
AssertTrue(observationPrompt.Contains("Tool observation 1/3"), "AgentRunner labels tool observations with step count");
AssertTrue(observationPrompt.Contains("one more strict tool_call"), "AgentRunner allows another tool before the limit");
string finalObservationPrompt = AgentRunner.BuildToolObservationPrompt("datetime", ToolResult.Ok("UTC now"), 3, 3);
AssertTrue(finalObservationPrompt.Contains("Do not request another tool"), "AgentRunner blocks further tools at the step limit");

var scriptedChatClient = new ScriptedChatClient([
    "{\"type\":\"tool_call\",\"tool\":\"calculator\",\"input\":\"25 * 19\"}",
    "{\"type\":\"tool_call\",\"tool\":\"datetime\",\"input\":\"\"}",
    "The calculation is 475, and I also checked the current time."
]);
var multiStepRunner = new AgentRunner(scriptedChatClient, new ToolRegistry([calculator, dateTimeTool]), maxToolIterations: 3);
string multiStepAnswer = await multiStepRunner.RunAsync([new OllamaMessageDto("user", "Calculate 25 * 19 and then check the time.")], CancellationToken.None);
AssertTrue(multiStepAnswer.Contains("475"), "AgentRunner returns final answer after multiple tool observations");
AssertEqual(3, scriptedChatClient.Calls, "AgentRunner asks model again after each safe tool observation until final answer");
AssertTrue(scriptedChatClient.ModelTaskKinds.All(x => x == ModelTaskKind.Chat), "AgentRunner uses chat model route for normal/tool-loop chat calls");

var searchFinalChatClient = new ScriptedChatClient(["Search-grounded final answer"]);
var searchFinalRunner = new AgentRunner(searchFinalChatClient, new ToolRegistry([new FakeSearchTool()]), maxToolIterations: 3);
string searchFinalAnswer = await searchFinalRunner.RunAsync([new OllamaMessageDto("user", "what is the newest mitsubishi")], CancellationToken.None);
AssertEqual("Search-grounded final answer", searchFinalAnswer, "AgentRunner returns final search synthesis answer");
AssertEqual(ModelTaskKind.ToolFinalAnswer, searchFinalChatClient.ModelTaskKinds.Single(), "AgentRunner uses tool-final model route for online-search final synthesis");

DateTime scheduleNow = new(2026, 6, 26, 12, 0, 0, DateTimeKind.Utc);
AssertTrue(ScheduleParser.TryParse("2026-06-28 18:30", scheduleNow, out ScheduleParseResult absoluteSchedule), "ScheduleParser parses yyyy-MM-dd HH:mm");
AssertEqual(new DateTime(2026, 6, 28, 18, 30, 0, DateTimeKind.Utc), absoluteSchedule.ScheduledAtUtc, "ScheduleParser returns UTC absolute schedule time");
AssertEqual("2026-06-28 18:30 UTC", absoluteSchedule.DisplayText, "ScheduleParser renders absolute schedule display");

AssertTrue(ScheduleParser.TryParse("tomorrow 09:15", scheduleNow, out ScheduleParseResult tomorrowSchedule), "ScheduleParser parses tomorrow HH:mm");
AssertEqual(new DateTime(2026, 6, 27, 9, 15, 0, DateTimeKind.Utc), tomorrowSchedule.ScheduledAtUtc, "ScheduleParser schedules tomorrow at requested UTC time");

AssertTrue(ScheduleParser.TryParse("in 30m", scheduleNow, out ScheduleParseResult inMinutesSchedule), "ScheduleParser parses in Nm");
AssertEqual(scheduleNow.AddMinutes(30), inMinutesSchedule.ScheduledAtUtc, "ScheduleParser schedules relative minutes");

AssertTrue(ScheduleParser.TryParse("in 2h", scheduleNow, out ScheduleParseResult inHoursSchedule), "ScheduleParser parses in Nh");
AssertEqual(scheduleNow.AddHours(2), inHoursSchedule.ScheduledAtUtc, "ScheduleParser schedules relative hours");

AssertFalse(ScheduleParser.TryParse("yesterday 09:00", scheduleNow, out _), "ScheduleParser rejects unsupported natural language");
AssertFalse(ScheduleParser.TryParse("in 0m", scheduleNow, out _), "ScheduleParser rejects zero delay");
AssertFalse(ScheduleParser.TryParse("2026-06-26 11:59", scheduleNow, out _), "ScheduleParser rejects past absolute time");

var searchDisabledSettings = new BotSettings(
    BotToken: "test-token",
    OllamaUrl: "http://localhost:11434/api/chat",
    OllamaModel: "llama3.2:3b",
    OllamaEmbeddingUrl: "http://localhost:11434/api/embed",
    OllamaEmbeddingModel: "nomic-embed-text",
    EnableDocumentEmbeddings: false,
    EnableOnlineSearch: false,
    AdminChatId: 0,
    AllowedChatIds: new HashSet<long>(),
    AllowPublicAccess: false,
    DatabaseConnectionString: "test-db",
    ApplyMigrations: true,
    LogMessageContent: false,
    ConversationMaxHistory: 8,
    SearchRoutingMode: "heuristic",
    EnableSafeCommandTools: false,
    SafeCommandProjectRoot: Directory.GetCurrentDirectory());
var searchEnabledSettings = searchDisabledSettings with { EnableOnlineSearch = true };

var defaultModelRouting = new ModelRoutingService(searchDisabledSettings);
AssertEqual("llama3.2:3b", defaultModelRouting.GetModel(ModelTaskKind.Chat), "ModelRoutingService defaults chat route to OLLAMA_MODEL");
AssertEqual("llama3.2:3b", defaultModelRouting.GetModel(ModelTaskKind.Planning), "ModelRoutingService defaults planning route to OLLAMA_MODEL");
AssertEqual("llama3.2:3b", defaultModelRouting.GetModel(ModelTaskKind.DocumentQuestionAnswering), "ModelRoutingService defaults document QA route to OLLAMA_MODEL");
AssertTrue(defaultModelRouting.RenderSummary().Contains("chat=llama3.2:3b"), "ModelRoutingService summary includes chat route");

var routedModelSettings = searchDisabledSettings with
{
    OllamaPlanningModel = "qwen3:4b",
    OllamaDocumentQuestionAnsweringModel = "qwen3:8b",
    OllamaDocumentSummaryModel = "qwen3:8b",
    OllamaToolFinalModel = "qwen3:0.6b-fast",
    OllamaImageModel = "llava:latest",
    OllamaVoiceModel = "qwen3:4b-voice"
};
var routedModelService = new ModelRoutingService(routedModelSettings);
AssertEqual("llama3.2:3b", routedModelService.GetModel(ModelTaskKind.Chat), "ModelRoutingService keeps chat on base model when chat override is blank");
AssertEqual("qwen3:4b", routedModelService.GetModel(ModelTaskKind.Planning), "ModelRoutingService uses planning override");
AssertEqual("qwen3:8b", routedModelService.GetModel(ModelTaskKind.DocumentQuestionAnswering), "ModelRoutingService uses document QA override");
AssertEqual("qwen3:8b", routedModelService.GetModel(ModelTaskKind.DocumentSummary), "ModelRoutingService uses document summary override");
AssertEqual("qwen3:0.6b-fast", routedModelService.GetModel(ModelTaskKind.ToolFinalAnswer), "ModelRoutingService uses tool final-answer override");
AssertEqual("llava:latest", routedModelService.GetModel(ModelTaskKind.Image), "ModelRoutingService uses image override");
AssertEqual("qwen3:4b-voice", routedModelService.GetModel(ModelTaskKind.Voice), "ModelRoutingService uses voice override");
AssertTrue(routedModelService.RenderSummary().Contains("plan=qwen3:4b"), "ModelRoutingService summary includes planning route");

var registry = new ToolRegistry([
    dateTimeTool,
    calculator,
    new OnlineSearchTool(new HttpClient()),
    new BotStatusTool(searchEnabledSettings)
]);
ToolRegistry privacyGatedRegistry = ToolRegistryFactory.Create(searchDisabledSettings, new HttpClient());
AssertFalse(privacyGatedRegistry.TryGet("online_search", out _), "ToolRegistryFactory excludes online_search by default");
AssertFalse(privacyGatedRegistry.TryGet("git_status", out _), "ToolRegistryFactory excludes safe command tools by default");
AssertFalse(privacyGatedRegistry.RenderToolInstructions().Contains("git_status"), "Disabled safe command tools are not advertised in model instructions");
ToolRegistry safeCommandRegistry = ToolRegistryFactory.Create(searchDisabledSettings with
{
    EnableSafeCommandTools = true,
    SafeCommandProjectRoot = Directory.GetCurrentDirectory()
}, new HttpClient());
AssertTrue(safeCommandRegistry.TryGet("git_status", out IAgentTool? gitStatusTool), "ToolRegistryFactory includes git_status when safe command tools are enabled");
AssertTrue(safeCommandRegistry.TryGet("git_diff", out _), "ToolRegistryFactory includes git_diff when safe command tools are enabled");
AssertTrue(safeCommandRegistry.TryGet("git_log_recent", out _), "ToolRegistryFactory includes git_log_recent when safe command tools are enabled");
AssertTrue(safeCommandRegistry.TryGet("run_dotnet_tests", out IAgentTool? runDotnetTestsTool), "ToolRegistryFactory includes run_dotnet_tests when safe command tools are enabled");
AssertFalse(gitStatusTool!.RequiresApproval, "git_status is read-only and does not require approval");
AssertFalse(runDotnetTestsTool!.RequiresApproval, "run_dotnet_tests uses a fixed bounded command and does not require approval");
AssertTrue(safeCommandRegistry.RenderToolInstructions().Contains("{\"target\":\"helper-tests\"}"), "ToolRegistry instructions document run_dotnet_tests strict JSON input");
ToolResult gitStatusResult = await gitStatusTool.ExecuteAsync(string.Empty, CancellationToken.None);
AssertTrue(gitStatusResult.Success, "git_status runs successfully in the project root");
AssertTrue(gitStatusResult.Output.Contains("git status", StringComparison.OrdinalIgnoreCase), "git_status output identifies the command");
ToolResult invalidTestTargetResult = await runDotnetTestsTool.ExecuteAsync("{\"target\":\"all\"}", CancellationToken.None);
AssertFalse(invalidTestTargetResult.Success, "run_dotnet_tests rejects unsupported test targets");
AssertTrue(invalidTestTargetResult.Output.Contains("helper-tests", StringComparison.OrdinalIgnoreCase), "run_dotnet_tests rejection explains the allowed target");
ToolResult malformedTestTargetResult = await runDotnetTestsTool.ExecuteAsync("helper-tests", CancellationToken.None);
AssertFalse(malformedTestTargetResult.Success, "run_dotnet_tests rejects non-JSON input");
ToolRegistry enabledSearchRegistry = ToolRegistryFactory.Create(searchEnabledSettings, new HttpClient());
AssertTrue(enabledSearchRegistry.TryGet("online_search", out _), "ToolRegistryFactory includes online_search only when enabled");
AssertTrue(enabledSearchRegistry.RenderToolInstructions().Contains("online_search"), "Enabled online_search is advertised in model instructions");
AssertFalse(registry.RenderToolInstructions().Contains("Mitsubateie", StringComparison.OrdinalIgnoreCase), "ToolRegistry online_search instructions avoid domain-specific typo examples");
AssertFalse(registry.RenderToolInstructions().Contains("Mitsubishi Lancer", StringComparison.OrdinalIgnoreCase), "ToolRegistry online_search instructions avoid hardcoded correction examples");
AssertTrue(registry.TryGet("calculator", out IAgentTool? registeredCalculator), "ToolRegistry finds calculator");
AssertEqual("calculator", registeredCalculator!.Name, "ToolRegistry returns matching tool");
AssertTrue(registry.RenderToolList().Contains("online_search"), "ToolRegistry lists online search");

IReadOnlyList<AgentHarnessDefinition> harnesses = AgentHarnessCatalog.GetDefaultHarnesses();
AssertEqual(2, harnesses.Count, "AgentHarnessCatalog defines image and voice harnesses");
AssertTrue(harnesses.Any(x => x.Name == "image_agent"), "AgentHarnessCatalog includes image agent harness");
AssertTrue(harnesses.Any(x => x.Name == "voice_agent"), "AgentHarnessCatalog includes voice agent harness");
AssertTrue(harnesses.Single(x => x.Name == "image_agent").Tools.Any(x => x.Contains("describe_image", StringComparison.OrdinalIgnoreCase)), "Image harness lists describe_image tool");
AssertTrue(harnesses.Single(x => x.Name == "voice_agent").Tools.Any(x => x.Contains("transcribe_audio", StringComparison.OrdinalIgnoreCase)), "Voice harness lists transcribe_audio tool");
string renderedHarnesses = AgentHarnessCatalog.RenderHarnesses(harnesses);
AssertTrue(renderedHarnesses.Contains("P2 Agent Harness Plan"), "AgentHarnessCatalog renders a P2 title");
AssertTrue(renderedHarnesses.Contains("image_agent"), "AgentHarnessCatalog render includes image harness");
AssertTrue(renderedHarnesses.Contains("voice_agent"), "AgentHarnessCatalog render includes voice harness");
var harnessesCommand = new HarnessesCommand();
CommandResult harnessesCommandResult = await harnessesCommand.TryHandleAsync(
    new Message { Text = "/harnesses" },
    new ConnectedUser { ChatId = 123, FirstName = "Test" },
    null!,
    CancellationToken.None);
AssertTrue(harnessesCommandResult.Handled, "HarnessesCommand handles /harnesses");
AssertTrue(harnessesCommandResult.ReplyText?.Contains("image_agent") == true, "HarnessesCommand reply includes image harness");
AssertTrue(harnessesCommandResult.ReplyText?.Contains("voice_agent") == true, "HarnessesCommand reply includes voice harness");
AssertFalse((await harnessesCommand.TryHandleAsync(new Message { Text = "/harnessesx" }, new ConnectedUser { ChatId = 123 }, null!, CancellationToken.None)).Handled, "HarnessesCommand uses exact command matching");

Uri searchUri = OnlineSearchTool.BuildSearchUri("asp.net core performance tips");
AssertTrue(searchUri.ToString().Contains("startpage.com"), "OnlineSearchTool primary endpoint uses Startpage compatibility helper");
AssertTrue(searchUri.Query.Contains("asp.net"), "OnlineSearchTool includes query text");
IReadOnlyList<string> typoSearchVariants = OnlineSearchTool.BuildSearchQueryVariants("Mitsubateie Lanser 1992");
AssertTrue(typoSearchVariants.Contains("Mitsubateie Lanser 1992"), "OnlineSearchTool preserves the user's original query instead of applying domain-specific typo corrections");
AssertFalse(typoSearchVariants.Any(x => x.Contains("Mitsubishi Lancer", StringComparison.OrdinalIgnoreCase)), "OnlineSearchTool does not hardcode Mitsubishi/Lancer typo correction");
AssertTrue(typoSearchVariants.Any(x => x.Contains("price", StringComparison.OrdinalIgnoreCase) && x.Contains("spec", StringComparison.OrdinalIgnoreCase)), "OnlineSearchTool still expands year-based vehicle searches with price/spec terms");

string startPageFixture = """
<a class="result-title result-link" href="https://www.microsoft.com/en-us/surface/devices/surface-laptop"><h2>Surface Laptop 8th Edition - Microsoft</h2></a>
<p class="description">Processor. 8-core Snapdragon X Plus. Battery life. Up to 23 hours.</p>
""";
IReadOnlyList<SearchResult> parsedSearchResults = OnlineSearchTool.ParseSearchHtml(startPageFixture);
AssertEqual(1, parsedSearchResults.Count, "OnlineSearchTool parses Startpage-style results");
AssertTrue(parsedSearchResults[0].Title.Contains("Surface Laptop 8th"), "OnlineSearchTool parses result title");
AssertTrue(parsedSearchResults[0].Snippet.Contains("Snapdragon"), "OnlineSearchTool parses result snippet");
string readablePageText = OnlineSearchTool.ExtractReadablePageText("<html><head><script>ignore()</script></head><body><h1>New Mitsubishi</h1><p>Official latest model details.</p></body></html>");
AssertTrue(readablePageText.Contains("New Mitsubishi"), "OnlineSearchTool extracts readable page text");
AssertFalse(readablePageText.Contains("ignore()"), "OnlineSearchTool removes script content from page extracts");
string renderedSearchWithExtract = OnlineSearchTool.RenderResults(
    "newest mitsubishi",
    "newest mitsubishi 2026 official latest model",
    [new SearchResult("Mitsubishi Motors: What's new for 2026", "https://example.com/mitsubishi", "Official 2026 lineup")],
    "fixture",
    [new PageExtract("Mitsubishi Motors: What's new for 2026", "https://example.com/mitsubishi", "The 2026 lineup includes updated Outlander details.")]);
AssertTrue(renderedSearchWithExtract.Contains("Read page extracts"), "OnlineSearchTool renders read page extracts");
AssertTrue(renderedSearchWithExtract.Contains("updated Outlander"), "OnlineSearchTool includes page extract text in tool output");

string consolePanel = AgentConsoleRenderer.RenderStartupPanel(new AgentConsoleSnapshot(
    BotUsername: "test_bot",
    OllamaUrl: "http://localhost:11434/api/chat",
    OllamaModel: "llama3.2:3b",
    DatabaseConnection: "LocalDB",
    AccessMode: "public override",
    MessageContentLoggingEnabled: false,
    OnlineSearchEnabled: true,
    ApplyMigrations: true,
    Commands: ["/help", "/status", "/tools"],
    Tools: registry.Tools.Select(x => x.Name).ToList()));
AssertTrue(consolePanel.Contains("TelegramMessagingTool Agent Console"), "Console renderer has title");
AssertTrue(consolePanel.Contains("/tools"), "Console renderer lists commands");
AssertTrue(consolePanel.Contains("online_search"), "Console renderer lists tools");
AssertTrue(consolePanel.Contains("Quick start"), "Console renderer shows quick start guidance");
AssertTrue(consolePanel.Contains("Type directly in this console"), "Console renderer shows console and Telegram usage examples");
AssertTrue(consolePanel.Contains("/exit"), "Console renderer documents console exit command");
AssertTrue(consolePanel.Contains("Safety warnings"), "Console renderer shows safety warning section");
AssertTrue(consolePanel.Contains("ALLOW_PUBLIC_ACCESS is enabled"), "Console renderer warns when public access override is enabled");
AssertTrue(consolePanel.Contains("Anyone who finds the bot can use it"), "Console renderer explains public override risk");

string lockedConsolePanel = AgentConsoleRenderer.RenderStartupPanel(new AgentConsoleSnapshot(
    BotUsername: "test_bot",
    OllamaUrl: "http://localhost:11434/api/chat",
    OllamaModel: "llama3.2:3b",
    DatabaseConnection: "LocalDB",
    AccessMode: "locked",
    MessageContentLoggingEnabled: false,
    OnlineSearchEnabled: false,
    ApplyMigrations: true,
    Commands: ["/help"],
    Tools: ["calculator"]));
AssertTrue(lockedConsolePanel.Contains("Telegram access is locked"), "Console renderer warns when bot is locked");
AssertTrue(lockedConsolePanel.Contains("Online search disabled"), "Console renderer shows online search disabled quick-start note");
AssertFalse(lockedConsolePanel.Contains("online_search"), "Console renderer does not list online_search when disabled");

string adminOnlyConsolePanel = AgentConsoleRenderer.RenderStartupPanel(new AgentConsoleSnapshot(
    BotUsername: "test_bot",
    OllamaUrl: "http://localhost:11434/api/chat",
    OllamaModel: "llama3.2:3b",
    DatabaseConnection: "LocalDB",
    AccessMode: "admin-only",
    MessageContentLoggingEnabled: false,
    OnlineSearchEnabled: false,
    ApplyMigrations: true,
    Commands: ["/help"],
    Tools: ["calculator"]));
AssertTrue(adminOnlyConsolePanel.Contains("No immediate safety warnings."), "Console renderer does not warn for admin-only mode");
AssertFalse(adminOnlyConsolePanel.Contains("Anyone who finds the bot can use it"), "Console renderer does not show public warning for admin-only mode");

string maskedConnection = AgentConsoleRenderer.SummarizeDatabaseConnection("Server=(localdb)\\MSSQLLocalDB;Database=TelegramMessagingTool;User Id=admin;Password=secret-password;TrustServerCertificate=True");
AssertTrue(maskedConnection.Contains("Database=TelegramMessagingTool"), "Database connection summary keeps useful database name");
AssertFalse(maskedConnection.Contains("secret-password"), "Database connection summary masks password");
AssertFalse(maskedConnection.Contains("User Id=admin"), "Database connection summary hides user id");

string eventLine = AgentConsoleRenderer.RenderEvent("MESSAGE", "tester", "handled with tools", ConsoleEventLevel.Success);
AssertTrue(eventLine.Contains("MESSAGE"), "Console event includes label");
AssertTrue(eventLine.Contains("tester"), "Console event includes actor");
AssertTrue(eventLine.Contains("handled with tools"), "Console event includes detail");

AssertEqual("1.5 GB", LocalDeviceInfoService.FormatBytes(1_610_612_736), "LocalDeviceInfoService formats byte counts safely");
string systemInfoText = LocalDeviceInfoService.RenderSystemInfo();
AssertTrue(systemInfoText.Contains("Operating system"), "LocalDeviceInfoService renders OS information");
AssertTrue(systemInfoText.Contains("Machine name"), "LocalDeviceInfoService renders machine name");
string diskStatusText = LocalDeviceInfoService.RenderDiskStatus();
AssertTrue(diskStatusText.Contains("Disk status"), "LocalDeviceInfoService renders disk status");
string processText = LocalDeviceInfoService.RenderTopProcesses(5);
AssertTrue(processText.Contains("Running processes"), "LocalDeviceInfoService renders process list");
AssertTrue(processText.Length < 4096, "LocalDeviceInfoService process list is Telegram-safe");

string testFileRoot = Path.Combine(Path.GetTempPath(), "TelegramMessagingTool_FileTests_" + Guid.NewGuid().ToString("N"));
var documentStorage = new DocumentStorageService(testFileRoot, maxFileBytes: 1024 * 1024);
string importDirectory = Path.Combine(testFileRoot, "ImportInbox");
Directory.CreateDirectory(importDirectory);
var testEmbeddingService = new DeterministicEmbeddingService();
var documentEmbeddingService = new DocumentEmbeddingService(testEmbeddingService, "test-embedding-model");
AssertEqual("nomic-embed-text", BotConfiguration.NormalizeEmbeddingModel(""), "BotConfiguration defaults embedding model");
AssertTrue(BotConfiguration.IsEnabled("true", defaultValue: false), "BotConfiguration parses enabled flag");
string? previousAllowPublicAccess = Environment.GetEnvironmentVariable("ALLOW_PUBLIC_ACCESS");
string? previousAllowedChatIdsForConfig = Environment.GetEnvironmentVariable("ALLOWED_CHAT_IDS");
string? previousEnableOnlineSearch = Environment.GetEnvironmentVariable("ENABLE_ONLINE_SEARCH");
string? previousOllamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
string? previousOllamaModelPlan = Environment.GetEnvironmentVariable("OLLAMA_MODEL_PLAN");
string? previousOllamaModelDocQa = Environment.GetEnvironmentVariable("OLLAMA_MODEL_DOC_QA");
try
{
    Environment.SetEnvironmentVariable("ALLOW_PUBLIC_ACCESS", null);
    Environment.SetEnvironmentVariable("ALLOWED_CHAT_IDS", null);
    Environment.SetEnvironmentVariable("ENABLE_ONLINE_SEARCH", null);
    Environment.SetEnvironmentVariable("OLLAMA_MODEL", "base-model:test");
    Environment.SetEnvironmentVariable("OLLAMA_MODEL_PLAN", null);
    Environment.SetEnvironmentVariable("OLLAMA_MODEL_DOC_QA", null);
    BotSettings defaultPrivacySettings = BotConfiguration.LoadFromEnvironment();
    AssertFalse(defaultPrivacySettings.AllowPublicAccess, "BotConfiguration defaults public access override to false");
    AssertFalse(defaultPrivacySettings.EnableOnlineSearch, "BotConfiguration defaults online search to disabled");
    AssertEqual("base-model:test", defaultPrivacySettings.OllamaChatModel, "BotConfiguration defaults chat route to OLLAMA_MODEL");
    AssertEqual("base-model:test", defaultPrivacySettings.OllamaPlanningModel, "BotConfiguration defaults planning route to OLLAMA_MODEL");

    Environment.SetEnvironmentVariable("ALLOW_PUBLIC_ACCESS", "yes");
    AssertTrue(BotConfiguration.LoadFromEnvironment().AllowPublicAccess, "BotConfiguration parses ALLOW_PUBLIC_ACCESS truthy values");

    Environment.SetEnvironmentVariable("ENABLE_ONLINE_SEARCH", "true");
    AssertTrue(BotConfiguration.LoadFromEnvironment().EnableOnlineSearch, "BotConfiguration parses ENABLE_ONLINE_SEARCH truthy values");

    Environment.SetEnvironmentVariable("OLLAMA_MODEL_PLAN", "plan-model:test");
    Environment.SetEnvironmentVariable("OLLAMA_MODEL_DOC_QA", "docqa-model:test");
    BotSettings routedEnvironmentSettings = BotConfiguration.LoadFromEnvironment();
    AssertEqual("base-model:test", routedEnvironmentSettings.OllamaChatModel, "BotConfiguration keeps chat model on OLLAMA_MODEL when chat override is blank");
    AssertEqual("plan-model:test", routedEnvironmentSettings.OllamaPlanningModel, "BotConfiguration loads OLLAMA_MODEL_PLAN");
    AssertEqual("docqa-model:test", routedEnvironmentSettings.OllamaDocumentQuestionAnsweringModel, "BotConfiguration loads OLLAMA_MODEL_DOC_QA");
}
finally
{
    Environment.SetEnvironmentVariable("ALLOW_PUBLIC_ACCESS", previousAllowPublicAccess);
    Environment.SetEnvironmentVariable("ALLOWED_CHAT_IDS", previousAllowedChatIdsForConfig);
    Environment.SetEnvironmentVariable("ENABLE_ONLINE_SEARCH", previousEnableOnlineSearch);
    Environment.SetEnvironmentVariable("OLLAMA_MODEL", previousOllamaModel);
    Environment.SetEnvironmentVariable("OLLAMA_MODEL_PLAN", previousOllamaModelPlan);
    Environment.SetEnvironmentVariable("OLLAMA_MODEL_DOC_QA", previousOllamaModelDocQa);
}
AssertEqual("report.md", DocumentStorageService.SanitizeFileName("..\\..//report.md"), "SanitizeFileName removes path segments");
AssertTrue(documentStorage.IsAllowedFileName("notes.txt"), "DocumentStorageService allows txt files");
AssertTrue(documentStorage.IsAllowedFileName("report.md"), "DocumentStorageService allows markdown files");
AssertTrue(documentStorage.IsAllowedFileName("manual.pdf"), "DocumentStorageService allows PDF files");
AssertTrue(documentStorage.IsAllowedFileName("brief.docx"), "DocumentStorageService allows DOCX files");
AssertTrue(documentStorage.IsAllowedFileName("table.xlsx"), "DocumentStorageService allows XLSX files");
AssertTrue(documentStorage.IsAllowedFileName("photo.png"), "DocumentStorageService allows PNG image files");
AssertTrue(documentStorage.IsAllowedFileName("photo.jpg"), "DocumentStorageService allows JPG image files");
AssertTrue(DocumentStorageService.IsImageFileName("photo.webp"), "DocumentStorageService recognizes WEBP image files");
AssertFalse(DocumentStorageService.IsImageFileName("manual.pdf"), "DocumentStorageService does not classify PDFs as images");
AssertFalse(documentStorage.IsAllowedFileName("malware.exe"), "DocumentStorageService rejects executable files");

var storageTestUser = new ConnectedUser { Id = 42, ChatId = 123456789, Name = "tester" };
UploadedFile createdFile = await documentStorage.CreateTextFileAsync(storageTestUser, "summary.md", "# Summary\nHello document", CancellationToken.None);
AssertTrue(File.Exists(createdFile.AbsolutePath), "CreateTextFileAsync writes sandboxed file");
AssertTrue(createdFile.RelativePath.Contains("123456789"), "CreateTextFileAsync stores under chat sandbox");
AssertFalse(createdFile.RelativePath.Contains(".."), "CreateTextFileAsync does not store traversal path");
string extractedText = await documentStorage.ExtractTextAsync(createdFile, CancellationToken.None);
AssertTrue(extractedText.Contains("Hello document"), "ExtractTextAsync reads created text document");

UploadedFile createdDocx = await documentStorage.CreateFileAsync(storageTestUser, "brief.docx", "DOCX capability works", CancellationToken.None);
string extractedDocx = await documentStorage.ExtractTextAsync(createdDocx, CancellationToken.None);
AssertTrue(extractedDocx.Contains("DOCX capability works"), "ExtractTextAsync reads created DOCX document");

UploadedFile createdXlsx = await documentStorage.CreateFileAsync(storageTestUser, "table.xlsx", "Name,Value\nExcel capability,42", CancellationToken.None);
string extractedXlsx = await documentStorage.ExtractTextAsync(createdXlsx, CancellationToken.None);
AssertTrue(extractedXlsx.Contains("Excel capability"), "ExtractTextAsync reads created XLSX workbook");

UploadedFile createdPdf = await documentStorage.CreateFileAsync(storageTestUser, "manual.pdf", "PDF capability works", CancellationToken.None);
string extractedPdf = await documentStorage.ExtractTextAsync(createdPdf, CancellationToken.None);
AssertTrue(extractedPdf.Contains("PDF capability works"), "ExtractTextAsync reads created PDF document");

IReadOnlyList<string> documentChunks = DocumentChunker.Split(string.Join(" ", Enumerable.Repeat("alpha beta gamma delta", 500)), chunkSize: 300, overlap: 50);
AssertTrue(documentChunks.Count > 1, "DocumentChunker splits long text into multiple chunks");
AssertTrue(documentChunks.All(x => x.Length <= 300), "DocumentChunker respects maximum chunk size");
AssertEqual(0, DocumentChunker.Split("   ").Count, "DocumentChunker ignores blank text");

var retrievalService = new DocumentRetrievalService();
IReadOnlyList<DocumentChunk> rankedChunks = DocumentRetrievalService.RankChunks([
    new DocumentChunk { Id = 1, OriginalFileName = "a.txt", ChunkNumber = 1, Text = "payment deadline is Sunday" },
    new DocumentChunk { Id = 2, OriginalFileName = "b.txt", ChunkNumber = 1, Text = "shipping details only" }
], "what is the payment deadline?", limit: 1);
AssertEqual(1, rankedChunks.Count, "DocumentRetrievalService returns requested limit");
AssertTrue(rankedChunks[0].Text.Contains("payment deadline"), "DocumentRetrievalService ranks relevant chunks first");

IReadOnlyList<DocumentChunk> phraseRankedChunks = DocumentRetrievalService.RankChunks([
    new DocumentChunk { Id = 1, OriginalFileName = "a.txt", ChunkNumber = 1, Text = "payment deadline appears in one exact phrase" },
    new DocumentChunk { Id = 2, OriginalFileName = "b.txt", ChunkNumber = 1, Text = "payment terms are listed elsewhere and the deadline appears later" }
], "payment deadline", limit: 2);
AssertEqual(1, phraseRankedChunks[0].Id, "DocumentRetrievalService boosts exact phrase matches before loose term matches");

string serializedEmbedding = EmbeddingMath.Serialize([1.0, 0.0]);
AssertTrue(serializedEmbedding.Contains("1"), "EmbeddingMath serializes vectors");
IReadOnlyList<float> parsedEmbedding = EmbeddingMath.Parse(serializedEmbedding);
AssertEqual(2, parsedEmbedding.Count, "EmbeddingMath parses serialized vectors");
AssertTrue(EmbeddingMath.CosineSimilarity([1.0f, 0.0f], [1.0f, 0.0f]) > 0.99, "EmbeddingMath scores identical vectors highly");
AssertTrue(EmbeddingMath.CosineSimilarity([1.0f, 0.0f], [0.0f, 1.0f]) < 0.01, "EmbeddingMath scores unrelated vectors low");

IReadOnlyList<DocumentChunk> semanticRankedChunks = DocumentRetrievalService.RankChunksByHybridScore([
    new DocumentChunk { Id = 1, OriginalFileName = "a.txt", ChunkNumber = 1, Text = "contract terms", EmbeddingJson = EmbeddingMath.Serialize([1.0, 0.0]) },
    new DocumentChunk { Id = 2, OriginalFileName = "b.txt", ChunkNumber = 1, Text = "vacation plan", EmbeddingJson = EmbeddingMath.Serialize([0.0, 1.0]) }
], "holiday itinerary", [0.0f, 1.0f], limit: 1);
AssertEqual(2, semanticRankedChunks[0].Id, "DocumentRetrievalService can rank by stored embeddings when available");

AssertTrue(OllamaEmbeddingClient.BuildEmbedUrl("http://localhost:11434/api/chat").EndsWith("/api/embed"), "OllamaEmbeddingClient derives /api/embed from /api/chat");
AssertTrue(OllamaEmbeddingClient.TryParseEmbeddingResponse("{\"embeddings\":[[0.1,0.2,0.3]]}", out IReadOnlyList<float> parsedOllamaEmbedding), "OllamaEmbeddingClient parses /api/embed response");
AssertEqual(3, parsedOllamaEmbedding.Count, "OllamaEmbeddingClient returns parsed vector values");

string qaPrompt = DocumentQuestionAnsweringService.BuildPrompt(
    "what is the payment deadline?",
    [new DocumentChunk { UploadedFileId = 9, OriginalFileName = "contract.pdf", ChunkNumber = 2, Text = "The payment deadline is Sunday." }]);
AssertTrue(qaPrompt.Contains("Use ONLY the document excerpts"), "DocumentQuestionAnsweringService restricts answers to excerpts");
AssertTrue(qaPrompt.Contains("File #9 contract.pdf, chunk 2"), "DocumentQuestionAnsweringService includes chunk citation labels");

string summaryPrompt = DocumentSummaryService.BuildPrompt([
    new DocumentChunk { UploadedFileId = 9, OriginalFileName = "contract.pdf", ChunkNumber = 1, Text = "Contract payment details." }
]);
AssertTrue(summaryPrompt.Contains("Summarize the user's indexed document excerpts"), "DocumentSummaryService creates a summary prompt");
AssertTrue(summaryPrompt.Contains("File #9 contract.pdf, chunk 1"), "DocumentSummaryService includes chunk citation labels");

static Message TextMessage(string text) => new()
{
    Text = text,
    Chat = new Chat { Id = 123456789, Username = "tester", FirstName = "Test", LastName = "User" }
};

string testDbName = $"TelegramMessagingTool_CommandTests_{Guid.NewGuid():N}";
string previousConnection = Environment.GetEnvironmentVariable("TELEGRAM_DB_CONNECTION") ?? string.Empty;
Environment.SetEnvironmentVariable(
    "TELEGRAM_DB_CONNECTION",
    $@"Server=(localdb)\MSSQLLocalDB;Database={testDbName};Trusted_Connection=True;TrustServerCertificate=True");

await using (var dbContext = new TelegramDbContext())
{
    await dbContext.Database.MigrateAsync();

    var testUser = new ConnectedUser
    {
        ChatId = 123456789,
        Name = "tester",
        FirstName = "Test",
        LastName = "User",
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow
    };

    var nonAdminUser = new ConnectedUser
    {
        ChatId = 987654321,
        Name = "nonadmin",
        FirstName = "Non",
        LastName = "Admin",
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow
    };

    dbContext.Users.AddRange(testUser, nonAdminUser);
    await dbContext.SaveChangesAsync();

    var adminTestSettings = new BotSettings(
        BotToken: "test-token",
        OllamaUrl: "http://localhost:11434/api/chat",
        OllamaModel: "qwen3:0.6b",
        OllamaEmbeddingUrl: "http://localhost:11434/api/embed",
        OllamaEmbeddingModel: "nomic-embed-text",
        EnableDocumentEmbeddings: false,
        EnableOnlineSearch: false,
        AdminChatId: testUser.ChatId,
        AllowedChatIds: new HashSet<long>(),
        AllowPublicAccess: false,
        DatabaseConnectionString: Environment.GetEnvironmentVariable("TELEGRAM_DB_CONNECTION")!,
        ApplyMigrations: true,
        LogMessageContent: false,
        ConversationMaxHistory: 8,
        SearchRoutingMode: "heuristic",
        EnableSafeCommandTools: false,
        SafeCommandProjectRoot: Directory.GetCurrentDirectory())
    {
        OllamaPlanningModel = "qwen3:4b",
        OllamaDocumentQuestionAnsweringModel = "qwen3:8b"
    };

    var pendingActionService = new PendingActionService();
    var fakeProcessTerminator = new FakeProcessTerminator();
    var pendingActionExecutor = new PendingActionExecutor(fakeProcessTerminator, documentStorage);
    var agentTaskService = new AgentTaskService();
    var documentIndexingService = new DocumentIndexingService(documentStorage);
    var documentRetrievalService = new DocumentRetrievalService(testEmbeddingService);
    var documentQaChatClient = new ScriptedChatClient([
        "The payment deadline is Sunday. Source: File #1 notes.md, chunk 1.",
        "The saved note says this is a saved note. Source: File #1 notes.md, chunk 1.",
        "Summary: this document contains a saved note. Source: File #1 notes.md, chunk 1.",
        "Summary: indexed documents include a saved note. Source: File #1 notes.md, chunk 1."
    ]);
    var documentQuestionAnsweringService = new DocumentQuestionAnsweringService(documentQaChatClient);
    var documentSummaryChatClient = new ScriptedChatClient([
        "Summary: this document contains a saved note. Source: File #1 notes.md, chunk 1.",
        "Summary: indexed documents include a saved note. Source: File #1 notes.md, chunk 1."
    ]);
    var documentSummaryService = new DocumentSummaryService(documentSummaryChatClient);
    var commandRouter = new CommandRouter([
        new HelpCommand(),
        new SystemInfoCommand(),
        new DiskStatusCommand(),
        new ProcessesCommand(),
        new StatusCommand(adminTestSettings),
        new ResetCommand(),
        new RememberCommand(),
        new MemoryCommand(),
        new ForgetCommand(),
        new FilesCommand(documentStorage),
        new ImagesCommand(),
        new ReadFileCommand(documentStorage),
        new CreateFileCommand(documentStorage),
        new ImportFilesCommand(importDirectory, documentStorage, adminTestSettings),
        new ImportFileCommand(importDirectory, documentStorage, adminTestSettings),
        new DeleteFileCommand(pendingActionService, adminTestSettings),
        new IndexFileCommand(documentIndexingService),
        new IndexDocsCommand(documentIndexingService),
        new DocChunksCommand(),
        new AskFileCommand(documentIndexingService, documentRetrievalService, documentQuestionAnsweringService),
        new AskDocsCommand(documentRetrievalService, documentQuestionAnsweringService),
        new SummarizeFileCommand(documentIndexingService, documentRetrievalService, documentSummaryService),
        new SummarizeDocsCommand(documentRetrievalService, documentSummaryService),
        new EmbedFileCommand(documentIndexingService, documentEmbeddingService),
        new EmbedDocsCommand(documentIndexingService, documentEmbeddingService),
        new ToolsCommand(registry),
        new KillProcessCommand(pendingActionService, adminTestSettings),
        new ActionCommand(pendingActionService, adminTestSettings),
        new PendingCommand(pendingActionService, adminTestSettings),
        new ApproveCommand(pendingActionService, pendingActionExecutor, adminTestSettings),
        new DenyCommand(pendingActionService, adminTestSettings),
        new PlanCommand(agentTaskService),
        new TasksCommand(agentTaskService),
        new TaskCommand(agentTaskService),
        new ScheduleCommand(agentTaskService),
        new ScheduleListCommand(agentTaskService),
        new UnscheduleCommand(agentTaskService),
        new DoneCommand(agentTaskService),
        new CancelCommand(agentTaskService)
    ]);

    DateTime scheduledStepTime = new(2026, 6, 28, 18, 30, 0, DateTimeKind.Utc);
    var scheduledTask = new AgentTask
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        Goal = "Scheduled reminder persistence test",
        Status = AgentTaskStatuses.Active,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Steps =
        [
            new AgentTaskStep
            {
                StepNumber = 1,
                Description = "Prepare reminder storage.",
                ScheduledAtUtc = scheduledStepTime,
                ScheduleNote = "Review scheduled task storage",
                CreatedAt = DateTime.UtcNow
            },
            new AgentTaskStep
            {
                StepNumber = 2,
                Description = "Already reminded step.",
                ScheduledAtUtc = scheduledStepTime.AddHours(1),
                ReminderSentAtUtc = scheduledStepTime.AddHours(1).AddMinutes(5),
                CreatedAt = DateTime.UtcNow
            }
        ]
    };
    dbContext.AgentTasks.Add(scheduledTask);
    await dbContext.SaveChangesAsync(CancellationToken.None);

    AgentTask persistedScheduledTask = await dbContext.AgentTasks
        .Include(x => x.Steps)
        .SingleAsync(x => x.Id == scheduledTask.Id, CancellationToken.None);
    AgentTaskStep persistedScheduledStep = persistedScheduledTask.Steps.Single(x => x.StepNumber == 1);
    AssertEqual(scheduledStepTime, persistedScheduledStep.ScheduledAtUtc, "AgentTaskStep persists ScheduledAtUtc");
    AssertEqual("Review scheduled task storage", persistedScheduledStep.ScheduleNote, "AgentTaskStep persists ScheduleNote");
    AssertTrue(persistedScheduledTask.Steps.Single(x => x.StepNumber == 2).ReminderSentAtUtc.HasValue, "AgentTaskStep persists ReminderSentAtUtc");
    string renderedScheduledTask = AgentTaskService.RenderTask(persistedScheduledTask);
    AssertTrue(renderedScheduledTask.Contains("scheduled 2026-06-28 18:30 UTC", StringComparison.OrdinalIgnoreCase), "RenderTask shows scheduled step time");
    AssertTrue(renderedScheduledTask.Contains("Review scheduled task storage", StringComparison.OrdinalIgnoreCase), "RenderTask shows schedule note");
    AssertTrue(renderedScheduledTask.Contains("reminded 2026-06-28 19:35 UTC", StringComparison.OrdinalIgnoreCase), "RenderTask shows reminder sent time");
    dbContext.AgentTasks.Remove(persistedScheduledTask);
    await dbContext.SaveChangesAsync(CancellationToken.None);

    DateTime reminderNow = new(2026, 6, 28, 20, 0, 0, DateTimeKind.Utc);
    var dueReminderTask = new AgentTask
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        Goal = "Due reminder test",
        Status = AgentTaskStatuses.Active,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Steps =
        [
            new AgentTaskStep
            {
                StepNumber = 1,
                Description = "Send due reminder.",
                ScheduledAtUtc = reminderNow.AddMinutes(-5),
                ScheduleNote = "Important reminder note",
                CreatedAt = DateTime.UtcNow
            },
            new AgentTaskStep
            {
                StepNumber = 2,
                Description = "Future reminder.",
                ScheduledAtUtc = reminderNow.AddMinutes(5),
                CreatedAt = DateTime.UtcNow
            },
            new AgentTaskStep
            {
                StepNumber = 3,
                Description = "Already sent reminder.",
                ScheduledAtUtc = reminderNow.AddMinutes(-10),
                ReminderSentAtUtc = reminderNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow
            }
        ]
    };
    var completedDueReminderTask = new AgentTask
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        Goal = "Completed reminder test",
        Status = AgentTaskStatuses.Completed,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        CompletedAt = DateTime.UtcNow,
        Steps =
        [
            new AgentTaskStep
            {
                StepNumber = 1,
                Description = "Do not send completed task reminder.",
                ScheduledAtUtc = reminderNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow
            }
        ]
    };
    dbContext.AgentTasks.AddRange(dueReminderTask, completedDueReminderTask);
    await dbContext.SaveChangesAsync(CancellationToken.None);

    var fakeReminderSender = new FakeTaskReminderSender();
    var taskReminderService = new TaskReminderService(fakeReminderSender, () => reminderNow);
    ReminderScanResult reminderScanResult = await taskReminderService.SendDueRemindersAsync(dbContext, CancellationToken.None);
    AssertEqual(1, reminderScanResult.SentCount, "TaskReminderService sends one due unsent active reminder");
    AssertEqual(1, fakeReminderSender.SentMessages.Count, "TaskReminderService calls sender once");
    AssertEqual(testUser.ChatId, fakeReminderSender.SentMessages[0].ChatId, "TaskReminderService sends reminder to task chat");
    AssertTrue(fakeReminderSender.SentMessages[0].Text.Contains("Due reminder test"), "TaskReminderService reminder includes task goal");
    AssertTrue(fakeReminderSender.SentMessages[0].Text.Contains("Important reminder note"), "TaskReminderService reminder includes schedule note");
    AgentTaskStep dueReminderStep = await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == dueReminderTask.Id && x.StepNumber == 1, CancellationToken.None);
    AssertEqual(reminderNow, dueReminderStep.ReminderSentAtUtc, "TaskReminderService marks reminder sent after successful send");
    ReminderScanResult secondReminderScanResult = await taskReminderService.SendDueRemindersAsync(dbContext, CancellationToken.None);
    AssertEqual(0, secondReminderScanResult.SentCount, "TaskReminderService does not resend already marked reminders");
    AssertEqual(1, fakeReminderSender.SentMessages.Count, "TaskReminderService sender is not called again after reminder is marked sent");
    dbContext.AgentTasks.RemoveRange(dueReminderTask, completedDueReminderTask);
    await dbContext.SaveChangesAsync(CancellationToken.None);

    CommandResult helpResult = await commandRouter.TryHandleAsync(TextMessage("/help"), testUser, dbContext, CancellationToken.None);
    AssertTrue(helpResult.Handled, "/help is handled");
    AssertTrue(helpResult.ReplyText?.Contains("/remember") == true, "/help lists memory commands");
    AssertTrue(helpResult.ReplyMarkup is null, "/help has no inline keyboard markup by default");

    InlineKeyboardMarkup samplePendingMarkup = InlineKeyboardFactory.ForPendingAction(123);
    AssertTrue(samplePendingMarkup.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Approve" && button.CallbackData == "act:approve:123"), "InlineKeyboardFactory creates approve button");
    AssertTrue(samplePendingMarkup.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Deny" && button.CallbackData == "act:deny:123"), "InlineKeyboardFactory creates deny button");
    AssertTrue(samplePendingMarkup.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Details" && button.CallbackData == "act:details:123"), "InlineKeyboardFactory creates details button");

    BotSettings safeCommandAdminSettings = adminTestSettings with
    {
        EnableSafeCommandTools = true,
        SafeCommandProjectRoot = Directory.GetCurrentDirectory()
    };
    ToolRegistry approvalToolRegistry = ToolRegistryFactory.Create(safeCommandAdminSettings, new HttpClient(), pendingActionService);
    AssertTrue(approvalToolRegistry.TryGet("publish_release", out IAgentTool? publishReleaseTool), "ToolRegistryFactory includes publish_release approval request tool when pending service is provided");
    AssertTrue(approvalToolRegistry.TryGet("restart_latest_bot", out IAgentTool? restartLatestBotTool), "ToolRegistryFactory includes restart_latest_bot approval request tool when pending service is provided");
    AssertTrue(publishReleaseTool!.RequiresApproval, "publish_release requires approval");
    AssertTrue(restartLatestBotTool!.RequiresApproval, "restart_latest_bot requires approval");

    var releaseRequestChatClient = new ScriptedChatClient([
        "{\"type\":\"tool_call\",\"tool\":\"publish_release\",\"input\":\"{\\\"reason\\\":\\\"verify latest changes\\\"}\"}"
    ]);
    var releaseRequestRunner = new AgentRunner(
        releaseRequestChatClient,
        approvalToolRegistry,
        searchRoutingClassifier: new OffSearchRoutingClassifier());
    string releaseRequestReply = await releaseRequestRunner.RunAsync(
        [new OllamaMessageDto("user", "request a publish release approval")],
        CancellationToken.None,
        dbContext,
        testUser);
    AssertTrue(releaseRequestReply.Contains("Pending action #"), "AgentRunner creates a pending action for approval request tools");
    PendingAction publishReleaseAction = await dbContext.PendingActions.OrderByDescending(x => x.Id).FirstAsync(x => x.ToolName == "publish_release", CancellationToken.None);
    AssertEqual(PendingActionStatuses.Pending, publishReleaseAction.Status, "publish_release request is stored as pending");
    AssertTrue(publishReleaseAction.PayloadJson.Contains("verify latest changes"), "publish_release request stores the reason in payload JSON");

    PendingActionDecision publishApproval = await pendingActionService.ApproveAsync(dbContext, testUser, publishReleaseAction.Id, CancellationToken.None);
    AssertTrue(publishApproval.Success, "publish_release pending action can be approved");
    PendingActionExecutionResult publishExecution = await pendingActionExecutor.ExecuteApprovedAsync(dbContext, publishApproval.Action!, CancellationToken.None);
    AssertFalse(publishExecution.Executed, "publish_release approval does not execute automatically yet");
    AssertTrue(publishExecution.Message.Contains("No automatic execution", StringComparison.OrdinalIgnoreCase), "publish_release approval explains that no executor is registered");

    AssertTrue(PendingActionCallbackParser.TryParse("act:approve:123", out PendingActionCallback approveCallback), "PendingActionCallbackParser parses approve callback");
    AssertEqual(PendingActionCallbackVerb.Approve, approveCallback.Verb, "PendingActionCallbackParser reads approve verb");
    AssertEqual(123, approveCallback.ActionId, "PendingActionCallbackParser reads approve action id");
    AssertTrue(PendingActionCallbackParser.TryParse("act:deny:456", out PendingActionCallback denyCallback), "PendingActionCallbackParser parses deny callback");
    AssertEqual(PendingActionCallbackVerb.Deny, denyCallback.Verb, "PendingActionCallbackParser reads deny verb");
    AssertEqual(456, denyCallback.ActionId, "PendingActionCallbackParser reads deny action id");
    AssertTrue(PendingActionCallbackParser.TryParse("act:details:789", out PendingActionCallback detailsCallback), "PendingActionCallbackParser parses details callback");
    AssertEqual(PendingActionCallbackVerb.Details, detailsCallback.Verb, "PendingActionCallbackParser reads details verb");
    AssertEqual(789, detailsCallback.ActionId, "PendingActionCallbackParser reads details action id");
    AssertFalse(PendingActionCallbackParser.TryParse("/pending", out _), "PendingActionCallbackParser rejects slash commands");
    AssertFalse(PendingActionCallbackParser.TryParse("approve:123", out _), "PendingActionCallbackParser rejects missing domain prefix");
    AssertFalse(PendingActionCallbackParser.TryParse("act:approve:nope", out _), "PendingActionCallbackParser rejects non-numeric action ids");
    AssertFalse(PendingActionCallbackParser.TryParse("act:unknown:123", out _), "PendingActionCallbackParser rejects unknown verbs");
    AssertFalse(PendingActionCallbackParser.TryParse("act:approve", out _), "PendingActionCallbackParser rejects missing id");
    AssertFalse(PendingActionCallbackParser.TryParse("act:approve:123:extra", out _), "PendingActionCallbackParser rejects extra callback parts");

    CommandResult statusResult = await commandRouter.TryHandleAsync(TextMessage("/status"), testUser, dbContext, CancellationToken.None);
    AssertTrue(statusResult.Handled, "/status is handled");
    AssertTrue(statusResult.ReplyText?.Contains("Database: OK") == true, "/status reports database OK");
    AssertTrue(statusResult.ReplyText?.Contains("Access mode: admin-only") == true, "/status reports access mode");
    AssertTrue(statusResult.ReplyText?.Contains("Model routes:") == true, "/status reports model routes heading");
    AssertTrue(statusResult.ReplyText?.Contains("plan=qwen3:4b") == true, "/status reports planning model route");
    AssertTrue(statusResult.ReplyText?.Contains("doc_qa=qwen3:8b") == true, "/status reports document QA model route");
    AssertTrue(statusResult.ReplyText?.Contains("Search routing: heuristic") == true, "/status reports search routing mode");
    AssertTrue(statusResult.ReplyText?.Contains("Safe command tools: disabled") == true, "/status reports safe command tools mode");

    CommandResult statusMentionResult = await commandRouter.TryHandleAsync(TextMessage("/status@red_eye_ghost_bot"), testUser, dbContext, CancellationToken.None);
    AssertTrue(statusMentionResult.Handled, "/status@bot is handled");
    AssertTrue(statusMentionResult.ReplyText?.Contains("Database: OK") == true, "/status@bot reports database OK");

    CommandResult statusPrefixResult = await commandRouter.TryHandleAsync(TextMessage("/statusx"), testUser, dbContext, CancellationToken.None);
    AssertFalse(statusPrefixResult.Handled, "/statusx is not treated as /status");

    CommandResult toolsResult = await commandRouter.TryHandleAsync(TextMessage("/tools"), testUser, dbContext, CancellationToken.None);
    AssertTrue(toolsResult.Handled, "/tools is handled");
    AssertTrue(toolsResult.ReplyText?.Contains("online_search") == true, "/tools lists online search");

    CommandResult systemInfoResult = await commandRouter.TryHandleAsync(TextMessage("/systeminfo"), testUser, dbContext, CancellationToken.None);
    AssertTrue(systemInfoResult.Handled, "/systeminfo is handled");
    AssertTrue(systemInfoResult.ReplyText?.Contains("Operating system") == true, "/systeminfo reports OS info");

    CommandResult diskStatusResult = await commandRouter.TryHandleAsync(TextMessage("/diskstatus"), testUser, dbContext, CancellationToken.None);
    AssertTrue(diskStatusResult.Handled, "/diskstatus is handled");
    AssertTrue(diskStatusResult.ReplyText?.Contains("Disk status") == true, "/diskstatus reports disk info");

    CommandResult processesResult = await commandRouter.TryHandleAsync(TextMessage("/processes"), testUser, dbContext, CancellationToken.None);
    AssertTrue(processesResult.Handled, "/processes is handled");
    AssertTrue(processesResult.ReplyText?.Contains("Running processes") == true, "/processes reports process info");

    CommandResult invalidKillProcessResult = await commandRouter.TryHandleAsync(TextMessage("/killprocess nope"), testUser, dbContext, CancellationToken.None);
    AssertTrue(invalidKillProcessResult.Handled, "/killprocess invalid input is handled");
    AssertTrue(invalidKillProcessResult.ReplyText?.Contains("Usage: /killprocess <pid>") == true, "/killprocess validates PID input");

    CommandResult nonAdminKillProcessResult = await commandRouter.TryHandleAsync(TextMessage("/killprocess 12345"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminKillProcessResult.Handled, "/killprocess non-admin attempt is handled");
    AssertTrue(nonAdminKillProcessResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/killprocess requires admin");
    AssertEqual(0, await dbContext.PendingActions.CountAsync(x => x.ConnectedUserId == nonAdminUser.Id), "/killprocess non-admin does not create pending action");

    CommandResult killProcessResult = await commandRouter.TryHandleAsync(TextMessage("/killprocess 12345"), testUser, dbContext, CancellationToken.None);
    AssertTrue(killProcessResult.Handled, "/killprocess is handled");
    AssertTrue(killProcessResult.ReplyText?.Contains("approval", StringComparison.OrdinalIgnoreCase) == true, "/killprocess asks for approval instead of executing");
    PendingAction killProcessPendingAction = await dbContext.PendingActions.SingleAsync(x => x.ToolName == "kill_process", CancellationToken.None);
    AssertEqual("high", killProcessPendingAction.RiskLevel, "/killprocess creates high risk pending action");
    AssertTrue(killProcessPendingAction.PayloadJson.Contains("12345"), "/killprocess stores target PID in payload");

    CommandResult approveKillProcessResult = await commandRouter.TryHandleAsync(TextMessage($"/approve {killProcessPendingAction.Id}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(approveKillProcessResult.Handled, "/approve kill_process is handled");
    AssertTrue(approveKillProcessResult.ReplyText?.Contains("Execution result", StringComparison.OrdinalIgnoreCase) == true, "/approve kill_process reports execution result");
    AssertEqual(12345, fakeProcessTerminator.LastRequestedPid, "/approve kill_process executes approved PID through safe terminator");
    AssertEqual(1, fakeProcessTerminator.KillCallCount, "/approve kill_process executes once");
    AssertTrue((await dbContext.PendingActions.FindAsync([killProcessPendingAction.Id], CancellationToken.None))!.DecisionNote.Contains("terminated", StringComparison.OrdinalIgnoreCase), "/approve kill_process records execution result");

    CommandResult invalidActionResult = await commandRouter.TryHandleAsync(TextMessage("/action nope"), testUser, dbContext, CancellationToken.None);
    AssertTrue(invalidActionResult.Handled, "/action invalid input is handled");
    AssertTrue(invalidActionResult.ReplyText?.Contains("Usage: /action <pending-action-id>") == true, "/action validates action id input");

    CommandResult actionDetailsResult = await commandRouter.TryHandleAsync(TextMessage($"/action {killProcessPendingAction.Id}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(actionDetailsResult.Handled, "/action is handled");
    AssertTrue(actionDetailsResult.ReplyText?.Contains($"Action #{killProcessPendingAction.Id}") == true, "/action shows action id");
    AssertTrue(actionDetailsResult.ReplyText?.Contains("kill_process") == true, "/action shows action type");
    AssertTrue(actionDetailsResult.ReplyText?.Contains("Status: approved") == true, "/action shows action status");
    AssertTrue(actionDetailsResult.ReplyText?.Contains("Decision note") == true, "/action shows decision note");
    AssertTrue(actionDetailsResult.ReplyText?.Contains("Payload") == true, "/action shows payload summary");
    AssertTrue(actionDetailsResult.ReplyText?.Contains("12345") == true, "/action includes target PID payload");

    CommandResult nonAdminActionResult = await commandRouter.TryHandleAsync(TextMessage($"/action {killProcessPendingAction.Id}"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminActionResult.Handled, "/action non-admin attempt is handled");
    AssertTrue(nonAdminActionResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/action requires admin");

    PendingAction pendingAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "project_patch_file",
        "Patch a source file after user approval.",
        "{\"path\":\"Program.cs\"}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);

    CommandResult pendingResult = await commandRouter.TryHandleAsync(TextMessage("/pending"), testUser, dbContext, CancellationToken.None);
    AssertTrue(pendingResult.Handled, "/pending is handled");
    AssertTrue(pendingResult.ReplyText?.Contains($"#{pendingAction.Id}") == true, "/pending lists pending action");
    AssertTrue(pendingResult.ReplyMarkup is not null, "/pending includes inline action keyboard metadata");
    AssertTrue(pendingResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.CallbackData == $"act:approve:{pendingAction.Id}"), "/pending action keyboard includes approve callback");
    AssertTrue(pendingResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.CallbackData == $"act:deny:{pendingAction.Id}"), "/pending action keyboard includes deny callback");

    var pendingCallbackService = new PendingActionCallbackService(pendingActionService, pendingActionExecutor, adminTestSettings);
    PendingActionCallbackResult invalidCallbackResult = await pendingCallbackService.HandleAsync("task:open:1", testUser, dbContext, CancellationToken.None);
    AssertFalse(invalidCallbackResult.Handled, "PendingActionCallbackService ignores non-action callback domains");

    PendingActionCallbackResult detailsCallbackResult = await pendingCallbackService.HandleAsync($"act:details:{pendingAction.Id}", testUser, dbContext, CancellationToken.None);
    AssertTrue(detailsCallbackResult.Handled, "PendingActionCallbackService handles details callbacks");
    AssertTrue(detailsCallbackResult.AnswerText.Contains("Details"), "PendingActionCallbackService answers callback details requests");
    AssertTrue(detailsCallbackResult.MessageText?.Contains($"Action #{pendingAction.Id}") == true, "PendingActionCallbackService details includes action id");
    AssertEqual(PendingActionStatuses.Pending, (await dbContext.PendingActions.FindAsync([pendingAction.Id], CancellationToken.None))!.Status, "PendingActionCallbackService details does not decide action");

    PendingAction callbackDenyAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "callback_deny_test",
        "Deny from inline callback.",
        "{}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);
    PendingActionCallbackResult denyCallbackResult = await pendingCallbackService.HandleAsync($"act:deny:{callbackDenyAction.Id}", testUser, dbContext, CancellationToken.None);
    AssertTrue(denyCallbackResult.Handled, "PendingActionCallbackService handles deny callbacks");
    AssertTrue(denyCallbackResult.MessageText?.Contains("denied", StringComparison.OrdinalIgnoreCase) == true, "PendingActionCallbackService deny returns denial message");
    AssertEqual(PendingActionStatuses.Denied, (await dbContext.PendingActions.FindAsync([callbackDenyAction.Id], CancellationToken.None))!.Status, "PendingActionCallbackService deny marks action denied");

    PendingAction callbackApproveAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "kill_process",
        "Terminate PID 54321 after inline approval.",
        "{\"pid\":54321}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);
    int killCallsBeforeCallback = fakeProcessTerminator.KillCallCount;
    PendingActionCallbackResult approveCallbackResult = await pendingCallbackService.HandleAsync($"act:approve:{callbackApproveAction.Id}", testUser, dbContext, CancellationToken.None);
    AssertTrue(approveCallbackResult.Handled, "PendingActionCallbackService handles approve callbacks");
    AssertTrue(approveCallbackResult.MessageText?.Contains("Execution result", StringComparison.OrdinalIgnoreCase) == true, "PendingActionCallbackService approve reports execution result");
    AssertEqual(54321, fakeProcessTerminator.LastRequestedPid, "PendingActionCallbackService approve executes kill_process through safe terminator");
    AssertEqual(killCallsBeforeCallback + 1, fakeProcessTerminator.KillCallCount, "PendingActionCallbackService approve executes once");
    AssertEqual(PendingActionStatuses.Approved, (await dbContext.PendingActions.FindAsync([callbackApproveAction.Id], CancellationToken.None))!.Status, "PendingActionCallbackService approve marks action approved");

    PendingAction callbackNonAdminAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "callback_admin_test",
        "Admin-only callback action.",
        "{}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);
    PendingActionCallbackResult nonAdminCallbackResult = await pendingCallbackService.HandleAsync($"act:deny:{callbackNonAdminAction.Id}", nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminCallbackResult.Handled, "PendingActionCallbackService handles non-admin callback attempts");
    AssertTrue(nonAdminCallbackResult.MessageText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "PendingActionCallbackService requires admin");
    AssertEqual(PendingActionStatuses.Pending, (await dbContext.PendingActions.FindAsync([callbackNonAdminAction.Id], CancellationToken.None))!.Status, "PendingActionCallbackService non-admin does not decide action");

    CommandResult nonAdminPendingResult = await commandRouter.TryHandleAsync(TextMessage("/pending"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminPendingResult.Handled, "/pending non-admin attempt is handled");
    AssertTrue(nonAdminPendingResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/pending requires admin");

    CommandResult nonAdminApproveResult = await commandRouter.TryHandleAsync(TextMessage($"/approve {pendingAction.Id}"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminApproveResult.Handled, "/approve non-admin attempt is handled");
    AssertTrue(nonAdminApproveResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/approve requires admin");
    AssertEqual(PendingActionStatuses.Pending, (await dbContext.PendingActions.FindAsync([pendingAction.Id], CancellationToken.None))!.Status, "/approve non-admin does not approve action");

    CommandResult approveResult = await commandRouter.TryHandleAsync(TextMessage($"/approve {pendingAction.Id}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(approveResult.Handled, "/approve is handled");
    AssertEqual(PendingActionStatuses.Approved, (await dbContext.PendingActions.FindAsync([pendingAction.Id], CancellationToken.None))!.Status, "/approve marks action approved");
    AssertTrue(approveResult.ReplyText?.Contains("No automatic execution", StringComparison.OrdinalIgnoreCase) == true, "/approve does not execute unknown action types");
    AssertEqual(killCallsBeforeCallback + 1, fakeProcessTerminator.KillCallCount, "/approve does not call process terminator for non-kill actions");

    PendingAction secondPendingAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "git_push",
        "Push committed changes after approval.",
        "{}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);

    CommandResult denyResult = await commandRouter.TryHandleAsync(TextMessage($"/deny {secondPendingAction.Id}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(denyResult.Handled, "/deny is handled");
    AssertEqual(PendingActionStatuses.Denied, (await dbContext.PendingActions.FindAsync([secondPendingAction.Id], CancellationToken.None))!.Status, "/deny marks action denied");

    PendingAction thirdPendingAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "database_mutation",
        "Mutate a database record after approval.",
        "{}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);

    CommandResult nonAdminDenyResult = await commandRouter.TryHandleAsync(TextMessage($"/deny {thirdPendingAction.Id}"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminDenyResult.Handled, "/deny non-admin attempt is handled");
    AssertTrue(nonAdminDenyResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/deny requires admin");
    AssertEqual(PendingActionStatuses.Pending, (await dbContext.PendingActions.FindAsync([thirdPendingAction.Id], CancellationToken.None))!.Status, "/deny non-admin does not deny action");

    CommandResult planResult = await commandRouter.TryHandleAsync(TextMessage("/plan build a small inventory API"), testUser, dbContext, CancellationToken.None);
    AssertTrue(planResult.Handled, "/plan is handled");
    AssertEqual(1, await dbContext.AgentTasks.CountAsync(x => x.ConnectedUserId == testUser.Id), "/plan creates task");
    int taskId = await dbContext.AgentTasks.Where(x => x.ConnectedUserId == testUser.Id).Select(x => x.Id).SingleAsync();
    AssertEqual(5, await dbContext.AgentTaskSteps.CountAsync(x => x.AgentTaskId == taskId), "/plan creates default task steps");

    CommandResult tasksResult = await commandRouter.TryHandleAsync(TextMessage("/tasks"), testUser, dbContext, CancellationToken.None);
    AssertTrue(tasksResult.Handled, "/tasks is handled");
    AssertTrue(tasksResult.ReplyText?.Contains("inventory API", StringComparison.OrdinalIgnoreCase) == true, "/tasks lists planned goal");
    AssertTrue(tasksResult.ReplyMarkup is not null, "/tasks includes inline task keyboard metadata");
    AssertTrue(tasksResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Open" && button.CallbackData == $"task:open:{taskId}"), "/tasks keyboard includes open callback for first task");

    AssertTrue(TaskCallbackParser.TryParse($"task:open:{taskId}", out TaskCallback openTaskCallback), "TaskCallbackParser parses open callback");
    AssertEqual(TaskCallbackVerb.Open, openTaskCallback.Verb, "TaskCallbackParser reads open verb");
    AssertEqual(taskId, openTaskCallback.TaskId, "TaskCallbackParser reads open task id");
    AssertTrue(TaskCallbackParser.TryParse($"task:done:{taskId}", out TaskCallback doneTaskCallback), "TaskCallbackParser parses done callback");
    AssertEqual(TaskCallbackVerb.Done, doneTaskCallback.Verb, "TaskCallbackParser reads done verb");
    AssertTrue(TaskCallbackParser.TryParse($"task:done-step:{taskId}:1", out TaskCallback doneStepTaskCallback), "TaskCallbackParser parses done-step callback");
    AssertEqual(TaskCallbackVerb.DoneStep, doneStepTaskCallback.Verb, "TaskCallbackParser reads done-step verb");
    AssertEqual(taskId, doneStepTaskCallback.TaskId, "TaskCallbackParser reads done-step task id");
    AssertEqual(1, doneStepTaskCallback.StepNumber, "TaskCallbackParser reads done-step step number");
    AssertTrue(TaskCallbackParser.TryParse($"task:cancel:{taskId}", out TaskCallback cancelTaskCallback), "TaskCallbackParser parses cancel callback");
    AssertEqual(TaskCallbackVerb.Cancel, cancelTaskCallback.Verb, "TaskCallbackParser reads cancel verb");
    AssertFalse(TaskCallbackParser.TryParse("act:details:1", out _), "TaskCallbackParser rejects pending-action domain");
    AssertFalse(TaskCallbackParser.TryParse("task:unknown:1", out _), "TaskCallbackParser rejects unknown task verb");
    AssertFalse(TaskCallbackParser.TryParse("task:open:nope", out _), "TaskCallbackParser rejects non-numeric task id");
    AssertFalse(TaskCallbackParser.TryParse("task:open", out _), "TaskCallbackParser rejects missing task id");
    AssertFalse(TaskCallbackParser.TryParse("task:open:1:extra", out _), "TaskCallbackParser rejects extra callback parts");
    AssertFalse(TaskCallbackParser.TryParse("task:done-step:1", out _), "TaskCallbackParser rejects done-step without step number");
    AssertFalse(TaskCallbackParser.TryParse("task:done-step:1:nope", out _), "TaskCallbackParser rejects non-numeric done-step step number");

    InlineKeyboardMarkup taskDetailsMarkup = InlineKeyboardFactory.ForTaskDetails(taskId);
    AssertTrue(taskDetailsMarkup.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Done" && button.CallbackData == $"task:done:{taskId}"), "InlineKeyboardFactory creates task done button");
    AssertTrue(taskDetailsMarkup.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Cancel" && button.CallbackData == $"task:cancel:{taskId}"), "InlineKeyboardFactory creates task cancel button");

    CommandResult taskResult = await commandRouter.TryHandleAsync(TextMessage($"/task {taskId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(taskResult.Handled, "/task is handled");
    AssertTrue(taskResult.ReplyText?.Contains("[ ] 1.") == true, "/task shows task steps");
    AssertTrue(taskResult.ReplyMarkup is not null, "/task includes inline task action keyboard metadata");
    AssertTrue(taskResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.CallbackData == $"task:done:{taskId}"), "/task keyboard includes done callback");
    AssertTrue(taskResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.CallbackData == $"task:done-step:{taskId}:1"), "/task keyboard includes done-step callback for step 1");
    AssertTrue(taskResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.CallbackData == $"task:cancel:{taskId}"), "/task keyboard includes cancel callback");

    var taskCallbackService = new TaskCallbackService(agentTaskService);
    TaskCallbackResult invalidTaskCallbackResult = await taskCallbackService.HandleAsync("act:details:1", testUser, dbContext, CancellationToken.None);
    AssertFalse(invalidTaskCallbackResult.Handled, "TaskCallbackService ignores non-task callback domains");
    TaskCallbackResult openTaskCallbackResult = await taskCallbackService.HandleAsync($"task:open:{taskId}", testUser, dbContext, CancellationToken.None);
    AssertTrue(openTaskCallbackResult.Handled, "TaskCallbackService handles open callbacks");
    AssertTrue(openTaskCallbackResult.AnswerText.Contains("Opened"), "TaskCallbackService answers open callbacks");
    AssertTrue(openTaskCallbackResult.MessageText?.Contains($"Task #{taskId}") == true, "TaskCallbackService open returns task details");
    AssertTrue(openTaskCallbackResult.MessageText?.Contains("inventory API", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService open includes task goal");
    TaskCallbackResult missingStepTaskCallbackResult = await taskCallbackService.HandleAsync($"task:done-step:{taskId}:99", testUser, dbContext, CancellationToken.None);
    AssertTrue(missingStepTaskCallbackResult.Handled, "TaskCallbackService handles missing done-step callbacks safely");
    AssertTrue(missingStepTaskCallbackResult.MessageText?.Contains("does not have step 99", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService reports missing step");
    AssertFalse((await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 1, CancellationToken.None)).IsDone, "TaskCallbackService missing done-step callback does not mutate other steps");
    TaskCallbackResult doneStepTaskCallbackResult = await taskCallbackService.HandleAsync($"task:done-step:{taskId}:1", testUser, dbContext, CancellationToken.None);
    AssertTrue(doneStepTaskCallbackResult.Handled, "TaskCallbackService handles done-step callbacks safely");
    AssertTrue(doneStepTaskCallbackResult.AnswerText.Contains("Done", StringComparison.OrdinalIgnoreCase), "TaskCallbackService answers successful done-step callbacks");
    AssertTrue(doneStepTaskCallbackResult.MessageText?.Contains("step 1 marked done", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService reports done-step success");
    AssertTrue(doneStepTaskCallbackResult.MessageText?.Contains("[x] 1.", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService returns updated task details after done-step");
    AssertTrue((await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 1, CancellationToken.None)).IsDone, "TaskCallbackService done-step callback marks the selected step done");
    AssertFalse((await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 2, CancellationToken.None)).IsDone, "TaskCallbackService done-step callback does not mark other steps done");
    AgentTask cancelCallbackTask = await agentTaskService.CreatePlanAsync(dbContext, testUser, "cancel from inline button", CancellationToken.None);
    TaskCallbackResult cancelTaskCallbackResult = await taskCallbackService.HandleAsync($"task:cancel:{cancelCallbackTask.Id}", testUser, dbContext, CancellationToken.None);
    AssertTrue(cancelTaskCallbackResult.Handled, "TaskCallbackService handles cancel callbacks safely");
    AssertTrue(cancelTaskCallbackResult.AnswerText.Contains("Cancelled", StringComparison.OrdinalIgnoreCase), "TaskCallbackService answers successful cancel callbacks");
    AssertTrue(cancelTaskCallbackResult.MessageText?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService reports cancel success");
    AssertEqual(AgentTaskStatuses.Cancelled, (await dbContext.AgentTasks.FindAsync([cancelCallbackTask.Id], CancellationToken.None))!.Status, "TaskCallbackService cancel callback cancels the selected task");
    AssertEqual(AgentTaskStatuses.Active, (await dbContext.AgentTasks.FindAsync([taskId], CancellationToken.None))!.Status, "TaskCallbackService cancel callback does not cancel a different task");

    AgentTask wholeDoneTask = await agentTaskService.CreatePlanAsync(dbContext, testUser, "ship the whole done button", CancellationToken.None);
    TaskCallbackResult doneTaskCallbackResult = await taskCallbackService.HandleAsync($"task:done:{wholeDoneTask.Id}", testUser, dbContext, CancellationToken.None);
    AssertTrue(doneTaskCallbackResult.Handled, "TaskCallbackService handles done callbacks safely");
    AssertTrue(doneTaskCallbackResult.AnswerText.Contains("Done", StringComparison.OrdinalIgnoreCase), "TaskCallbackService answers successful done callbacks");
    AssertTrue(doneTaskCallbackResult.MessageText?.Contains("marked completed", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService reports whole-task done success");
    AssertEqual(AgentTaskStatuses.Completed, (await dbContext.AgentTasks.FindAsync([wholeDoneTask.Id], CancellationToken.None))!.Status, "TaskCallbackService done callback completes the selected task");
    AssertEqual(5, await dbContext.AgentTaskSteps.CountAsync(x => x.AgentTaskId == wholeDoneTask.Id && x.IsDone, CancellationToken.None), "TaskCallbackService done callback marks all selected task steps done");
    AssertEqual(AgentTaskStatuses.Active, (await dbContext.AgentTasks.FindAsync([taskId], CancellationToken.None))!.Status, "TaskCallbackService done callback does not complete a different task");

    CommandResult invalidScheduleResult = await commandRouter.TryHandleAsync(TextMessage($"/schedule {taskId} 1 yesterday 09:00"), testUser, dbContext, CancellationToken.None);
    AssertTrue(invalidScheduleResult.Handled, "/schedule invalid time is handled");
    AssertTrue(invalidScheduleResult.ReplyText?.Contains("Usage: /schedule <task-id> <step-number> <time> [note]") == true, "/schedule invalid time explains usage");

    CommandResult scheduleResult = await commandRouter.TryHandleAsync(TextMessage($"/schedule {taskId} 1 in 30m Review the first step"), testUser, dbContext, CancellationToken.None);
    AssertTrue(scheduleResult.Handled, "/schedule is handled");
    AssertTrue(scheduleResult.ReplyText?.Contains("scheduled", StringComparison.OrdinalIgnoreCase) == true, "/schedule reports scheduled result");
    AgentTaskStep scheduledCommandStep = await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 1, CancellationToken.None);
    AssertTrue(scheduledCommandStep.ScheduledAtUtc.HasValue, "/schedule stores scheduled time");
    AssertTrue(scheduledCommandStep.ScheduledAtUtc > DateTime.UtcNow, "/schedule stores future scheduled time");
    AssertEqual("Review the first step", scheduledCommandStep.ScheduleNote, "/schedule stores note after schedule expression");

    CommandResult scheduleListResult = await commandRouter.TryHandleAsync(TextMessage("/schedulelist"), testUser, dbContext, CancellationToken.None);
    AssertTrue(scheduleListResult.Handled, "/schedulelist is handled");
    AssertTrue(scheduleListResult.ReplyText?.Contains($"Task #{taskId}") == true, "/schedulelist shows task id");
    AssertTrue(scheduleListResult.ReplyText?.Contains("Review the first step") == true, "/schedulelist shows schedule note");

    CommandResult unscheduleResult = await commandRouter.TryHandleAsync(TextMessage($"/unschedule {taskId} 1"), testUser, dbContext, CancellationToken.None);
    AssertTrue(unscheduleResult.Handled, "/unschedule is handled");
    AssertTrue(unscheduleResult.ReplyText?.Contains("unscheduled", StringComparison.OrdinalIgnoreCase) == true, "/unschedule reports cleared schedule");
    AgentTaskStep unscheduledCommandStep = await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 1, CancellationToken.None);
    AssertFalse(unscheduledCommandStep.ScheduledAtUtc.HasValue, "/unschedule clears scheduled time");
    AssertFalse(unscheduledCommandStep.ReminderSentAtUtc.HasValue, "/unschedule clears reminder sent time");
    AssertTrue(string.IsNullOrWhiteSpace(unscheduledCommandStep.ScheduleNote), "/unschedule clears schedule note");

    AssertFalse((await commandRouter.TryHandleAsync(TextMessage("/schedulex 1 1 in 30m"), testUser, dbContext, CancellationToken.None)).Handled, "/schedulex is not treated as /schedule");

    CommandResult doneStepResult = await commandRouter.TryHandleAsync(TextMessage($"/done {taskId} 1"), testUser, dbContext, CancellationToken.None);
    AssertTrue(doneStepResult.Handled, "/done step is handled");
    AssertTrue((await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 1)).IsDone, "/done marks step done");

    CommandResult cancelTaskResult = await commandRouter.TryHandleAsync(TextMessage($"/cancel {taskId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(cancelTaskResult.Handled, "/cancel is handled");
    AssertEqual(AgentTaskStatuses.Cancelled, (await dbContext.AgentTasks.FindAsync([taskId], CancellationToken.None))!.Status, "/cancel marks task cancelled");

    CommandResult createFileResult = await commandRouter.TryHandleAsync(TextMessage("/createfile notes.md This is a saved note"), testUser, dbContext, CancellationToken.None);
    AssertTrue(createFileResult.Handled, "/createfile is handled");
    AssertEqual(1, await dbContext.UploadedFiles.CountAsync(x => x.ConnectedUserId == testUser.Id), "/createfile saves file metadata");

    CommandResult filesResult = await commandRouter.TryHandleAsync(TextMessage("/files"), testUser, dbContext, CancellationToken.None);
    AssertTrue(filesResult.Handled, "/files is handled");
    AssertTrue(filesResult.ReplyText?.Contains("notes.md") == true, "/files lists created file");

    CommandResult emptyImagesResult = await commandRouter.TryHandleAsync(TextMessage("/images"), testUser, dbContext, CancellationToken.None);
    AssertTrue(emptyImagesResult.Handled, "/images is handled without saved images");
    AssertTrue(emptyImagesResult.ReplyText?.Contains("No images", StringComparison.OrdinalIgnoreCase) == true, "/images reports no saved images before image upload");

    int uploadedFileId = await dbContext.UploadedFiles
        .Where(x => x.ConnectedUserId == testUser.Id)
        .Select(x => x.Id)
        .SingleAsync();

    CommandResult readFileResult = await commandRouter.TryHandleAsync(TextMessage($"/readfile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(readFileResult.Handled, "/readfile is handled");
    AssertTrue(readFileResult.ReplyText?.Contains("This is a saved note") == true, "/readfile returns file contents");

    CommandResult indexFileResult = await commandRouter.TryHandleAsync(TextMessage($"/indexfile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(indexFileResult.Handled, "/indexfile is handled");
    AssertTrue(indexFileResult.ReplyText?.Contains("Chunks created") == true, "/indexfile reports chunk count");
    AssertTrue(await dbContext.DocumentChunks.AnyAsync(x => x.UploadedFileId == uploadedFileId), "/indexfile stores document chunks");

    CommandResult embedFileResult = await commandRouter.TryHandleAsync(TextMessage($"/embedfile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(embedFileResult.Handled, "/embedfile is handled");
    AssertTrue(embedFileResult.ReplyText?.Contains("Embedded chunks", StringComparison.OrdinalIgnoreCase) == true, "/embedfile reports embedded chunk count");
    AssertTrue(await dbContext.DocumentChunks.AnyAsync(x => x.UploadedFileId == uploadedFileId && x.EmbeddingJson != null), "/embedfile stores document embeddings");

    CommandResult embedDocsResult = await commandRouter.TryHandleAsync(TextMessage("/embeddocs"), testUser, dbContext, CancellationToken.None);
    AssertTrue(embedDocsResult.Handled, "/embeddocs is handled");
    AssertTrue(embedDocsResult.ReplyText?.Contains("Embedded chunks", StringComparison.OrdinalIgnoreCase) == true, "/embeddocs reports embedded chunk count");

    CommandResult docChunksResult = await commandRouter.TryHandleAsync(TextMessage($"/docchunks {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(docChunksResult.Handled, "/docchunks is handled");
    AssertTrue(docChunksResult.ReplyText?.Contains("Chunks:") == true, "/docchunks reports index status");

    CommandResult askFileResult = await commandRouter.TryHandleAsync(TextMessage($"/askfile {uploadedFileId} what does the note say?"), testUser, dbContext, CancellationToken.None);
    AssertTrue(askFileResult.Handled, "/askfile is handled");
    AssertTrue(askFileResult.ReplyText?.Contains("payment deadline", StringComparison.OrdinalIgnoreCase) == true, "/askfile returns model answer grounded in chunks");

    CommandResult askDocsResult = await commandRouter.TryHandleAsync(TextMessage("/askdocs what saved note do I have?"), testUser, dbContext, CancellationToken.None);
    AssertTrue(askDocsResult.Handled, "/askdocs is handled");
    AssertTrue(askDocsResult.ReplyText?.Contains("saved note", StringComparison.OrdinalIgnoreCase) == true, "/askdocs returns model answer across documents");
    AssertTrue(documentQaChatClient.ModelTaskKinds.All(x => x == ModelTaskKind.DocumentQuestionAnswering), "Document Q&A uses document QA model route");

    CommandResult summarizeFileResult = await commandRouter.TryHandleAsync(TextMessage($"/summarizefile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(summarizeFileResult.Handled, "/summarizefile is handled");
    AssertTrue(summarizeFileResult.ReplyText?.Contains("Summary", StringComparison.OrdinalIgnoreCase) == true, "/summarizefile returns a document summary");

    CommandResult summarizeDocsResult = await commandRouter.TryHandleAsync(TextMessage("/summarizedocs"), testUser, dbContext, CancellationToken.None);
    AssertTrue(summarizeDocsResult.Handled, "/summarizedocs is handled");
    AssertTrue(summarizeDocsResult.ReplyText?.Contains("indexed documents", StringComparison.OrdinalIgnoreCase) == true, "/summarizedocs returns an all-documents summary");
    AssertTrue(documentSummaryChatClient.ModelTaskKinds.All(x => x == ModelTaskKind.DocumentSummary), "Document summaries use document summary model route");

    await using var imageStream = new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]);
    UploadedFile uploadedImage = await documentStorage.SaveUploadedFileAsync(
        testUser,
        "sample.png",
        "telegram-image-file-id",
        "image/png",
        imageStream,
        imageStream.Length,
        CancellationToken.None);
    dbContext.UploadedFiles.Add(uploadedImage);
    await dbContext.SaveChangesAsync(CancellationToken.None);
    CommandResult imagesResult = await commandRouter.TryHandleAsync(TextMessage("/images"), testUser, dbContext, CancellationToken.None);
    AssertTrue(imagesResult.Handled, "/images is handled");
    AssertTrue(imagesResult.ReplyText?.Contains("sample.png") == true, "/images lists saved image files");
    AssertFalse(imagesResult.ReplyText?.Contains("notes.md") == true, "/images does not list non-image documents");

    CommandResult imagesMentionResult = await commandRouter.TryHandleAsync(TextMessage("/images@red_eye_ghost_bot"), testUser, dbContext, CancellationToken.None);
    AssertTrue(imagesMentionResult.Handled, "/images@bot is handled");
    AssertFalse((await commandRouter.TryHandleAsync(TextMessage("/imagesx"), testUser, dbContext, CancellationToken.None)).Handled, "/imagesx is not treated as /images");

    UploadedFile fileBeforeDelete = await dbContext.UploadedFiles.FirstAsync(x => x.Id == uploadedFileId, CancellationToken.None);
    string filePathBeforeDelete = fileBeforeDelete.AbsolutePath;
    AssertTrue(File.Exists(filePathBeforeDelete), "/deletefile test file exists before deletion approval");

    CommandResult invalidDeleteFileResult = await commandRouter.TryHandleAsync(TextMessage("/deletefile nope"), testUser, dbContext, CancellationToken.None);
    AssertTrue(invalidDeleteFileResult.Handled, "/deletefile invalid input is handled");
    AssertTrue(invalidDeleteFileResult.ReplyText?.Contains("Usage: /deletefile <file id>") == true, "/deletefile validates file id input");

    CommandResult nonAdminDeleteFileResult = await commandRouter.TryHandleAsync(TextMessage($"/deletefile {uploadedFileId}"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminDeleteFileResult.Handled, "/deletefile non-admin attempt is handled");
    AssertTrue(nonAdminDeleteFileResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/deletefile requires admin");

    CommandResult deleteFileResult = await commandRouter.TryHandleAsync(TextMessage($"/deletefile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(deleteFileResult.Handled, "/deletefile is handled");
    AssertTrue(deleteFileResult.ReplyText?.Contains("approval request", StringComparison.OrdinalIgnoreCase) == true, "/deletefile creates approval request");
    PendingAction deleteFilePendingAction = await dbContext.PendingActions.SingleAsync(x => x.ToolName == "delete_file", CancellationToken.None);
    AssertEqual("high", deleteFilePendingAction.RiskLevel, "/deletefile creates high risk pending action");
    AssertTrue(deleteFilePendingAction.PayloadJson.Contains(uploadedFileId.ToString()), "/deletefile stores target file id in payload");
    AssertTrue(File.Exists(filePathBeforeDelete), "/deletefile does not delete before approval");

    CommandResult approveDeleteFileResult = await commandRouter.TryHandleAsync(TextMessage($"/approve {deleteFilePendingAction.Id}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(approveDeleteFileResult.Handled, "/approve delete_file is handled");
    AssertTrue(approveDeleteFileResult.ReplyText?.Contains("Execution result", StringComparison.OrdinalIgnoreCase) == true, "/approve delete_file reports execution result");
    AssertFalse(File.Exists(filePathBeforeDelete), "/approve delete_file removes file from disk");
    AssertFalse(await dbContext.UploadedFiles.AnyAsync(x => x.Id == uploadedFileId, CancellationToken.None), "/approve delete_file removes file metadata");
    AssertFalse(await dbContext.DocumentChunks.AnyAsync(x => x.UploadedFileId == uploadedFileId, CancellationToken.None), "/approve delete_file removes indexed chunks");

    await File.WriteAllTextAsync(Path.Combine(importDirectory, "large-notes.md"), "Imported document content", CancellationToken.None);
    CommandResult importFilesResult = await commandRouter.TryHandleAsync(TextMessage("/importfiles"), testUser, dbContext, CancellationToken.None);
    AssertTrue(importFilesResult.Handled, "/importfiles is handled");
    AssertTrue(importFilesResult.ReplyText?.Contains("large-notes.md") == true, "/importfiles lists ImportInbox file");

    CommandResult nonAdminImportFileResult = await commandRouter.TryHandleAsync(TextMessage("/importfile large-notes.md"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminImportFileResult.Handled, "/importfile non-admin attempt is handled");
    AssertTrue(nonAdminImportFileResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/importfile requires admin");

    CommandResult traversalImportFileResult = await commandRouter.TryHandleAsync(TextMessage("/importfile ../large-notes.md"), testUser, dbContext, CancellationToken.None);
    AssertTrue(traversalImportFileResult.Handled, "/importfile traversal attempt is handled");
    AssertTrue(traversalImportFileResult.ReplyText?.Contains("plain filename", StringComparison.OrdinalIgnoreCase) == true, "/importfile rejects paths");

    CommandResult importFileResult = await commandRouter.TryHandleAsync(TextMessage("/importfile large-notes.md"), testUser, dbContext, CancellationToken.None);
    AssertTrue(importFileResult.Handled, "/importfile is handled");
    AssertTrue(importFileResult.ReplyText?.Contains("Imported file", StringComparison.OrdinalIgnoreCase) == true, "/importfile reports imported file");
    UploadedFile importedFile = await dbContext.UploadedFiles.SingleAsync(x => x.OriginalFileName == "large-notes.md", CancellationToken.None);
    AssertEqual("local_import", importedFile.Source, "/importfile stores local import source");
    AssertTrue(File.Exists(importedFile.AbsolutePath), "/importfile copies file into sandbox");
    AssertTrue(importedFile.AbsolutePath.StartsWith(documentStorage.RootDirectory, StringComparison.OrdinalIgnoreCase), "/importfile stores file under document sandbox");

    UploadedFile legacyOutsideSandboxFile = new()
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        OriginalFileName = "legacy.md",
        StoredFileName = "legacy.md",
        AbsolutePath = Path.Combine(Path.GetTempPath(), "TelegramMessagingTool_LegacyOutside_" + Guid.NewGuid().ToString("N"), "legacy.md"),
        RelativePath = "LegacyOutsideSandbox/legacy.md",
        ContentType = "text/markdown",
        SizeBytes = 12,
        Source = "legacy_test",
        CreatedAt = DateTime.UtcNow
    };
    dbContext.UploadedFiles.Add(legacyOutsideSandboxFile);
    await dbContext.SaveChangesAsync(CancellationToken.None);

    CommandResult legacyReadResult = await commandRouter.TryHandleAsync(TextMessage($"/readfile {legacyOutsideSandboxFile.Id}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(legacyReadResult.Handled, "/readfile legacy outside-sandbox file is handled");
    AssertTrue(legacyReadResult.ReplyText?.Contains("outside the current document sandbox", StringComparison.OrdinalIgnoreCase) == true, "/readfile reports outside-sandbox legacy file safely");

    CommandResult indexDocsWithLegacyResult = await commandRouter.TryHandleAsync(TextMessage("/indexdocs"), testUser, dbContext, CancellationToken.None);
    AssertTrue(indexDocsWithLegacyResult.Handled, "/indexdocs with legacy outside-sandbox file is handled");
    AssertTrue(indexDocsWithLegacyResult.ReplyText?.Contains("Skipped", StringComparison.OrdinalIgnoreCase) == true, "/indexdocs skips outside-sandbox legacy file safely");

    dbContext.Messages.Add(new ChatMessage
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        Content = "hello",
        Role = ChatRoles.User,
        Timestamp = DateTime.UtcNow
    });
    await dbContext.SaveChangesAsync();

    CommandResult resetResult = await commandRouter.TryHandleAsync(TextMessage("/reset"), testUser, dbContext, CancellationToken.None);
    AssertTrue(resetResult.Handled, "/reset is handled");
    AssertEqual(0, await dbContext.Messages.CountAsync(x => x.ConnectedUserId == testUser.Id), "/reset deletes user messages");

    CommandResult rememberResult = await commandRouter.TryHandleAsync(TextMessage("/remember User prefers concise answers"), testUser, dbContext, CancellationToken.None);
    AssertTrue(rememberResult.Handled, "/remember is handled");
    AssertEqual(1, await dbContext.Memories.CountAsync(x => x.ConnectedUserId == testUser.Id), "/remember saves memory");

    CommandResult mentionRememberResult = await commandRouter.TryHandleAsync(TextMessage("/remember@red_eye_ghost_bot User likes exact commands"), testUser, dbContext, CancellationToken.None);
    AssertTrue(mentionRememberResult.Handled, "/remember@bot is handled");
    AssertEqual(2, await dbContext.Memories.CountAsync(x => x.ConnectedUserId == testUser.Id), "/remember@bot saves memory using parsed arguments");

    CommandResult rememberPrefixResult = await commandRouter.TryHandleAsync(TextMessage("/remembered wrong command"), testUser, dbContext, CancellationToken.None);
    AssertFalse(rememberPrefixResult.Handled, "/remembered is not treated as /remember");

    CommandResult memoryResult = await commandRouter.TryHandleAsync(TextMessage("/memory"), testUser, dbContext, CancellationToken.None);
    AssertTrue(memoryResult.Handled, "/memory is handled");
    AssertTrue(memoryResult.ReplyText?.Contains("User prefers concise answers") == true, "/memory lists saved memory");

    int memoryId = await dbContext.Memories
        .Where(x => x.ConnectedUserId == testUser.Id)
        .Select(x => x.Id)
        .FirstAsync();

    CommandResult forgetResult = await commandRouter.TryHandleAsync(TextMessage($"/forget {memoryId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(forgetResult.Handled, "/forget is handled");
    AssertEqual(1, await dbContext.Memories.CountAsync(x => x.ConnectedUserId == testUser.Id), "/forget deletes one memory");
}

await using (var cleanupContext = new TelegramDbContext())
{
    await cleanupContext.Database.EnsureDeletedAsync();
}
Environment.SetEnvironmentVariable("TELEGRAM_DB_CONNECTION", previousConnection);

Console.WriteLine("All TelegramMessagingTool helper tests passed.");

sealed class DeterministicEmbeddingService : ITextEmbeddingService
{
    public Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        string normalized = text.ToLowerInvariant();
        if (normalized.Contains("saved") || normalized.Contains("note"))
        {
            return Task.FromResult<IReadOnlyList<float>>([0.0f, 1.0f]);
        }

        return Task.FromResult<IReadOnlyList<float>>([1.0f, 0.0f]);
    }
}

sealed class FakeProcessTerminator : IProcessTerminator
{
    public int? LastRequestedPid { get; private set; }

    public int KillCallCount { get; private set; }

    public ProcessTerminationResult Terminate(int processId)
    {
        LastRequestedPid = processId;
        KillCallCount++;
        return ProcessTerminationResult.Ok($"Process PID {processId} was terminated successfully.");
    }
}

sealed class FakeTaskReminderSender : ITaskReminderSender
{
    public List<(long ChatId, string Text)> SentMessages { get; } = [];

    public Task SendReminderAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        SentMessages.Add((chatId, text));
        return Task.CompletedTask;
    }
}

sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<string> _responses;

    public ScriptedChatClient(IEnumerable<string> responses)
    {
        _responses = new Queue<string>(responses);
    }

    public int Calls { get; private set; }

    public List<ModelTaskKind> ModelTaskKinds { get; } = [];

    public Task<string> AskAsync(
        List<OllamaMessageDto> conversationContext,
        CancellationToken cancellationToken,
        ModelTaskKind taskKind = ModelTaskKind.Chat)
    {
        Calls++;
        ModelTaskKinds.Add(taskKind);
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : "No scripted response left.");
    }
}

sealed class FakeSearchTool : IAgentTool
{
    public string Name => "online_search";

    public string Description => "Fake search tool for tests.";

    public bool RequiresApproval => false;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Ok($"Result for {input}\nhttps://example.com/source"));
    }
}
