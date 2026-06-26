using Microsoft.EntityFrameworkCore;
using System.Net.Sockets;
using Telegram.Bot.Types;
using TelegramMessagingTool;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Commands;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Tools;

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

AssertTrue(
    AgentRunner.TryBuildDirectSearchQuery([new OllamaMessageDto("user", "what is the newest car from mitsubishi")], out string newestMitsubishiQuery),
    "AgentRunner directly searches newest/current factual questions");
AssertTrue(newestMitsubishiQuery.Contains("Mitsubishi", StringComparison.OrdinalIgnoreCase), "AgentRunner keeps the requested brand in direct search query");
AssertTrue(newestMitsubishiQuery.Contains("official", StringComparison.OrdinalIgnoreCase), "AgentRunner expands newest/current direct search query with official/latest context");

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
    LogMessageContent: false);
var searchEnabledSettings = searchDisabledSettings with { EnableOnlineSearch = true };

var registry = new ToolRegistry([
    dateTimeTool,
    calculator,
    new OnlineSearchTool(new HttpClient()),
    new BotStatusTool(searchEnabledSettings)
]);
ToolRegistry privacyGatedRegistry = ToolRegistryFactory.Create(searchDisabledSettings, new HttpClient());
AssertFalse(privacyGatedRegistry.TryGet("online_search", out _), "ToolRegistryFactory excludes online_search by default");
AssertFalse(privacyGatedRegistry.RenderToolInstructions().Contains("online_search"), "Disabled online_search is not advertised in model instructions");
ToolRegistry enabledSearchRegistry = ToolRegistryFactory.Create(searchEnabledSettings, new HttpClient());
AssertTrue(enabledSearchRegistry.TryGet("online_search", out _), "ToolRegistryFactory includes online_search only when enabled");
AssertTrue(enabledSearchRegistry.RenderToolInstructions().Contains("online_search"), "Enabled online_search is advertised in model instructions");
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
AssertTrue(typoSearchVariants.Any(x => x.Contains("Mitsubishi Lancer 1992", StringComparison.OrdinalIgnoreCase)), "OnlineSearchTool corrects obvious Mitsubishi Lancer typo");
AssertTrue(typoSearchVariants.Any(x => x.Contains("price", StringComparison.OrdinalIgnoreCase) && x.Contains("spec", StringComparison.OrdinalIgnoreCase)), "OnlineSearchTool expands vehicle searches with price/spec terms");

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
try
{
    Environment.SetEnvironmentVariable("ALLOW_PUBLIC_ACCESS", null);
    Environment.SetEnvironmentVariable("ALLOWED_CHAT_IDS", null);
    Environment.SetEnvironmentVariable("ENABLE_ONLINE_SEARCH", null);
    BotSettings defaultPrivacySettings = BotConfiguration.LoadFromEnvironment();
    AssertFalse(defaultPrivacySettings.AllowPublicAccess, "BotConfiguration defaults public access override to false");
    AssertFalse(defaultPrivacySettings.EnableOnlineSearch, "BotConfiguration defaults online search to disabled");

    Environment.SetEnvironmentVariable("ALLOW_PUBLIC_ACCESS", "yes");
    AssertTrue(BotConfiguration.LoadFromEnvironment().AllowPublicAccess, "BotConfiguration parses ALLOW_PUBLIC_ACCESS truthy values");

    Environment.SetEnvironmentVariable("ENABLE_ONLINE_SEARCH", "true");
    AssertTrue(BotConfiguration.LoadFromEnvironment().EnableOnlineSearch, "BotConfiguration parses ENABLE_ONLINE_SEARCH truthy values");
}
finally
{
    Environment.SetEnvironmentVariable("ALLOW_PUBLIC_ACCESS", previousAllowPublicAccess);
    Environment.SetEnvironmentVariable("ALLOWED_CHAT_IDS", previousAllowedChatIdsForConfig);
    Environment.SetEnvironmentVariable("ENABLE_ONLINE_SEARCH", previousEnableOnlineSearch);
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
        LogMessageContent: false);

    var pendingActionService = new PendingActionService();
    var fakeProcessTerminator = new FakeProcessTerminator();
    var pendingActionExecutor = new PendingActionExecutor(fakeProcessTerminator, documentStorage);
    var agentTaskService = new AgentTaskService();
    var documentIndexingService = new DocumentIndexingService(documentStorage);
    var documentRetrievalService = new DocumentRetrievalService(testEmbeddingService);
    var documentQuestionAnsweringService = new DocumentQuestionAnsweringService(new ScriptedChatClient([
        "The payment deadline is Sunday. Source: File #1 notes.md, chunk 1.",
        "The saved note says this is a saved note. Source: File #1 notes.md, chunk 1.",
        "Summary: this document contains a saved note. Source: File #1 notes.md, chunk 1.",
        "Summary: indexed documents include a saved note. Source: File #1 notes.md, chunk 1."
    ]));
    var documentSummaryService = new DocumentSummaryService(new ScriptedChatClient([
        "Summary: this document contains a saved note. Source: File #1 notes.md, chunk 1.",
        "Summary: indexed documents include a saved note. Source: File #1 notes.md, chunk 1."
    ]));
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
        new DoneCommand(agentTaskService),
        new CancelCommand(agentTaskService)
    ]);

    CommandResult helpResult = await commandRouter.TryHandleAsync(TextMessage("/help"), testUser, dbContext, CancellationToken.None);
    AssertTrue(helpResult.Handled, "/help is handled");
    AssertTrue(helpResult.ReplyText?.Contains("/remember") == true, "/help lists memory commands");

    CommandResult statusResult = await commandRouter.TryHandleAsync(TextMessage("/status"), testUser, dbContext, CancellationToken.None);
    AssertTrue(statusResult.Handled, "/status is handled");
    AssertTrue(statusResult.ReplyText?.Contains("Database: OK") == true, "/status reports database OK");
    AssertTrue(statusResult.ReplyText?.Contains("Access mode: admin-only") == true, "/status reports access mode");

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
    AssertEqual(1, fakeProcessTerminator.KillCallCount, "/approve does not call process terminator for non-kill actions");

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

    CommandResult taskResult = await commandRouter.TryHandleAsync(TextMessage($"/task {taskId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(taskResult.Handled, "/task is handled");
    AssertTrue(taskResult.ReplyText?.Contains("[ ] 1.") == true, "/task shows task steps");

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

    CommandResult summarizeFileResult = await commandRouter.TryHandleAsync(TextMessage($"/summarizefile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(summarizeFileResult.Handled, "/summarizefile is handled");
    AssertTrue(summarizeFileResult.ReplyText?.Contains("Summary", StringComparison.OrdinalIgnoreCase) == true, "/summarizefile returns a document summary");

    CommandResult summarizeDocsResult = await commandRouter.TryHandleAsync(TextMessage("/summarizedocs"), testUser, dbContext, CancellationToken.None);
    AssertTrue(summarizeDocsResult.Handled, "/summarizedocs is handled");
    AssertTrue(summarizeDocsResult.ReplyText?.Contains("indexed documents", StringComparison.OrdinalIgnoreCase) == true, "/summarizedocs returns an all-documents summary");

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

sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<string> _responses;

    public ScriptedChatClient(IEnumerable<string> responses)
    {
        _responses = new Queue<string>(responses);
    }

    public int Calls { get; private set; }

    public Task<string> AskAsync(List<OllamaMessageDto> conversationContext, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : "No scripted response left.");
    }
}
