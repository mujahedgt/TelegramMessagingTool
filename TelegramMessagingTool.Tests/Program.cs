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
AssertTrue(BotAccessPolicy.IsAllowed(123, allowlist), "allowlist includes 123");
AssertTrue(BotAccessPolicy.IsAllowed(456, allowlist), "allowlist includes 456");
AssertFalse(BotAccessPolicy.IsAllowed(999, allowlist), "allowlist blocks unknown chat when configured");
AssertTrue(BotAccessPolicy.IsAllowed(999, new HashSet<long>()), "empty allowlist permits all for local development");

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
AssertTrue(promptWithMemory.Contains("request the `online_search` tool"), "BuildSystemPrompt tells model when to search");
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

var registry = new ToolRegistry([
    dateTimeTool,
    calculator,
    new OnlineSearchTool(new HttpClient()),
    new BotStatusTool(new BotSettings(
        BotToken: "test-token",
        OllamaUrl: "http://localhost:11434/api/chat",
        OllamaModel: "llama3.2:3b",
        AdminChatId: 0,
        AllowedChatIds: new HashSet<long>(),
        DatabaseConnectionString: "test-db",
        ApplyMigrations: true,
        LogMessageContent: false))
]);
AssertTrue(registry.TryGet("calculator", out IAgentTool? registeredCalculator), "ToolRegistry finds calculator");
AssertEqual("calculator", registeredCalculator!.Name, "ToolRegistry returns matching tool");
AssertTrue(registry.RenderToolList().Contains("online_search"), "ToolRegistry lists online search");

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
    AllowlistEnabled: false,
    MessageContentLoggingEnabled: false,
    ApplyMigrations: true,
    Commands: ["/help", "/status", "/tools"],
    Tools: registry.Tools.Select(x => x.Name).ToList()));
AssertTrue(consolePanel.Contains("TelegramMessagingTool Agent Console"), "Console renderer has title");
AssertTrue(consolePanel.Contains("/tools"), "Console renderer lists commands");
AssertTrue(consolePanel.Contains("online_search"), "Console renderer lists tools");
AssertTrue(consolePanel.Contains("Quick start"), "Console renderer shows quick start guidance");
AssertTrue(consolePanel.Contains("Ask Telegram"), "Console renderer shows Telegram usage examples");
AssertTrue(consolePanel.Contains("Safety warnings"), "Console renderer shows safety warning section");
AssertTrue(consolePanel.Contains("ALLOWED_CHAT_IDS is not set"), "Console renderer warns when allowlist is disabled");

string maskedConnection = AgentConsoleRenderer.SummarizeDatabaseConnection("Server=(localdb)\\MSSQLLocalDB;Database=TelegramMessagingTool;User Id=admin;Password=secret-password;TrustServerCertificate=True");
AssertTrue(maskedConnection.Contains("Database=TelegramMessagingTool"), "Database connection summary keeps useful database name");
AssertFalse(maskedConnection.Contains("secret-password"), "Database connection summary masks password");
AssertFalse(maskedConnection.Contains("User Id=admin"), "Database connection summary hides user id");

string eventLine = AgentConsoleRenderer.RenderEvent("MESSAGE", "tester", "handled with tools", ConsoleEventLevel.Success);
AssertTrue(eventLine.Contains("MESSAGE"), "Console event includes label");
AssertTrue(eventLine.Contains("tester"), "Console event includes actor");
AssertTrue(eventLine.Contains("handled with tools"), "Console event includes detail");

string testFileRoot = Path.Combine(Path.GetTempPath(), "TelegramMessagingTool_FileTests_" + Guid.NewGuid().ToString("N"));
var documentStorage = new DocumentStorageService(testFileRoot, maxFileBytes: 1024 * 1024);
AssertEqual("report.md", DocumentStorageService.SanitizeFileName("..\\..//report.md"), "SanitizeFileName removes path segments");
AssertTrue(documentStorage.IsAllowedFileName("notes.txt"), "DocumentStorageService allows txt files");
AssertTrue(documentStorage.IsAllowedFileName("report.md"), "DocumentStorageService allows markdown files");
AssertTrue(documentStorage.IsAllowedFileName("manual.pdf"), "DocumentStorageService allows PDF files");
AssertTrue(documentStorage.IsAllowedFileName("brief.docx"), "DocumentStorageService allows DOCX files");
AssertTrue(documentStorage.IsAllowedFileName("table.xlsx"), "DocumentStorageService allows XLSX files");
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

    dbContext.Users.Add(testUser);
    await dbContext.SaveChangesAsync();

    var pendingActionService = new PendingActionService();
    var commandRouter = new CommandRouter([
        new HelpCommand(),
        new StatusCommand(new BotSettings(
            BotToken: "test-token",
            OllamaUrl: "http://localhost:11434/api/chat",
            OllamaModel: "qwen3:0.6b",
            AdminChatId: 0,
            AllowedChatIds: new HashSet<long>(),
            DatabaseConnectionString: Environment.GetEnvironmentVariable("TELEGRAM_DB_CONNECTION")!,
            ApplyMigrations: true,
            LogMessageContent: false)),
        new ResetCommand(),
        new RememberCommand(),
        new MemoryCommand(),
        new ForgetCommand(),
        new FilesCommand(documentStorage),
        new ReadFileCommand(documentStorage),
        new CreateFileCommand(documentStorage),
        new ToolsCommand(registry),
        new PendingCommand(pendingActionService),
        new ApproveCommand(pendingActionService),
        new DenyCommand(pendingActionService)
    ]);

    CommandResult helpResult = await commandRouter.TryHandleAsync(TextMessage("/help"), testUser, dbContext, CancellationToken.None);
    AssertTrue(helpResult.Handled, "/help is handled");
    AssertTrue(helpResult.ReplyText?.Contains("/remember") == true, "/help lists memory commands");

    CommandResult statusResult = await commandRouter.TryHandleAsync(TextMessage("/status"), testUser, dbContext, CancellationToken.None);
    AssertTrue(statusResult.Handled, "/status is handled");
    AssertTrue(statusResult.ReplyText?.Contains("Database: OK") == true, "/status reports database OK");

    CommandResult toolsResult = await commandRouter.TryHandleAsync(TextMessage("/tools"), testUser, dbContext, CancellationToken.None);
    AssertTrue(toolsResult.Handled, "/tools is handled");
    AssertTrue(toolsResult.ReplyText?.Contains("online_search") == true, "/tools lists online search");

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

    CommandResult approveResult = await commandRouter.TryHandleAsync(TextMessage($"/approve {pendingAction.Id}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(approveResult.Handled, "/approve is handled");
    AssertEqual(PendingActionStatuses.Approved, (await dbContext.PendingActions.FindAsync([pendingAction.Id], CancellationToken.None))!.Status, "/approve marks action approved");

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

    CommandResult createFileResult = await commandRouter.TryHandleAsync(TextMessage("/createfile notes.md This is a saved note"), testUser, dbContext, CancellationToken.None);
    AssertTrue(createFileResult.Handled, "/createfile is handled");
    AssertEqual(1, await dbContext.UploadedFiles.CountAsync(x => x.ConnectedUserId == testUser.Id), "/createfile saves file metadata");

    CommandResult filesResult = await commandRouter.TryHandleAsync(TextMessage("/files"), testUser, dbContext, CancellationToken.None);
    AssertTrue(filesResult.Handled, "/files is handled");
    AssertTrue(filesResult.ReplyText?.Contains("notes.md") == true, "/files lists created file");

    int uploadedFileId = await dbContext.UploadedFiles
        .Where(x => x.ConnectedUserId == testUser.Id)
        .Select(x => x.Id)
        .SingleAsync();

    CommandResult readFileResult = await commandRouter.TryHandleAsync(TextMessage($"/readfile {uploadedFileId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(readFileResult.Handled, "/readfile is handled");
    AssertTrue(readFileResult.ReplyText?.Contains("This is a saved note") == true, "/readfile returns file contents");

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

    CommandResult memoryResult = await commandRouter.TryHandleAsync(TextMessage("/memory"), testUser, dbContext, CancellationToken.None);
    AssertTrue(memoryResult.Handled, "/memory is handled");
    AssertTrue(memoryResult.ReplyText?.Contains("User prefers concise answers") == true, "/memory lists saved memory");

    int memoryId = await dbContext.Memories
        .Where(x => x.ConnectedUserId == testUser.Id)
        .Select(x => x.Id)
        .SingleAsync();

    CommandResult forgetResult = await commandRouter.TryHandleAsync(TextMessage($"/forget {memoryId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(forgetResult.Handled, "/forget is handled");
    AssertEqual(0, await dbContext.Memories.CountAsync(x => x.ConnectedUserId == testUser.Id), "/forget deletes memory");
}

await using (var cleanupContext = new TelegramDbContext())
{
    await cleanupContext.Database.EnsureDeletedAsync();
}
Environment.SetEnvironmentVariable("TELEGRAM_DB_CONNECTION", previousConnection);

Console.WriteLine("All TelegramMessagingTool helper tests passed.");
