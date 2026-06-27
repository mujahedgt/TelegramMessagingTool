using TelegramMessagingTool;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Tools;

using var ollamaHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
using var searchHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

var settings = new BotSettings(
    BotToken: "test",
    OllamaUrl: "http://localhost:11434/api/chat",
    OllamaModel: "llama3.2:3b",
    OllamaEmbeddingUrl: "http://localhost:11434/api/embed",
    OllamaEmbeddingModel: BotConfiguration.DefaultEmbeddingModel,
    EnableDocumentEmbeddings: false,
    EnableOnlineSearch: true,
    AdminChatId: 0,
    AllowedChatIds: new HashSet<long>(),
    AllowPublicAccess: false,
    DatabaseConnectionString: "test",
    ApplyMigrations: false,
    LogMessageContent: false,
    ConversationMaxHistory: 8,
    SearchRoutingMode: "heuristic");

var registry = new ToolRegistry([
    new DateTimeTool(),
    new CalculatorTool(),
    new BotStatusTool(settings),
    new OnlineSearchTool(searchHttp)
]);

var runner = new AgentRunner(new OllamaChatClient(ollamaHttp, settings), registry);
var messages = new List<OllamaMessageDto>
{
    new("system", ConversationService.BuildSystemPrompt([], registry.RenderToolInstructions())),
    new("user", "ok searsh for \"Mitsubateie Lanser 1992\" and give me the prices and info about it")
};

string answer = await runner.RunAsync(messages, CancellationToken.None);
Console.WriteLine(answer);
