using Microsoft.EntityFrameworkCore;
using System.Net;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramMessagingTool;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Commands;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.models;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Tools;

BotSettings settings = BotConfiguration.LoadFromEnvironment();

if (string.IsNullOrWhiteSpace(settings.BotToken))
{
    Console.WriteLine("Missing TELEGRAM_BOT_TOKEN environment variable.");
    Console.WriteLine("Set it first, then restart the app.");
    return;
}

using HttpClient qwenClient = new()
{
    Timeout = TimeSpan.FromMinutes(30)
};

using HttpClient embeddingClient = new()
{
    Timeout = TimeSpan.FromMinutes(10)
};

using HttpClient searchClient = new()
{
    Timeout = TimeSpan.FromSeconds(20)
};

using SocketsHttpHandler telegramHttpHandler = new()
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    ConnectTimeout = TimeSpan.FromSeconds(30)
};

using HttpClient telegramHttpClient = new(telegramHttpHandler)
{
    Timeout = TimeSpan.FromSeconds(90)
};

var botClient = new TelegramBotClient(settings.BotToken, telegramHttpClient, CancellationToken.None);
var ollamaClient = new OllamaChatClient(qwenClient, settings);
var ollamaEmbeddingClient = new OllamaEmbeddingClient(embeddingClient, settings);
ITextEmbeddingService? retrievalEmbeddingService = settings.EnableDocumentEmbeddings ? ollamaEmbeddingClient : null;
var toolRegistry = ToolRegistryFactory.Create(settings, searchClient);
var documentStorage = new DocumentStorageService(Path.Combine(Environment.CurrentDirectory, "UserFiles"));
string importDirectory = Path.Combine(Environment.CurrentDirectory, "ImportInbox");
var pendingActionService = new PendingActionService();
var pendingActionExecutor = new PendingActionExecutor(new SystemProcessTerminator(), documentStorage);
var agentTaskService = new AgentTaskService();
var documentIndexingService = new DocumentIndexingService(documentStorage);
var documentEmbeddingService = new DocumentEmbeddingService(ollamaEmbeddingClient, settings.OllamaEmbeddingModel);
var documentRetrievalService = new DocumentRetrievalService(retrievalEmbeddingService);
var documentQuestionAnsweringService = new DocumentQuestionAnsweringService(ollamaClient);
var documentSummaryService = new DocumentSummaryService(ollamaClient);
var agentRunner = new AgentRunner(ollamaClient, toolRegistry);
var conversationService = new ConversationService();
var commandRouter = new CommandRouter([
    new HelpCommand(),
    new SystemInfoCommand(),
    new DiskStatusCommand(),
    new ProcessesCommand(),
    new StatusCommand(settings),
    new ResetCommand(),
    new RememberCommand(),
    new MemoryCommand(),
    new ForgetCommand(),
    new FilesCommand(documentStorage),
    new ImagesCommand(),
    new ReadFileCommand(documentStorage),
    new CreateFileCommand(documentStorage),
    new ImportFilesCommand(importDirectory, documentStorage, settings),
    new ImportFileCommand(importDirectory, documentStorage, settings),
    new DeleteFileCommand(pendingActionService, settings),
    new IndexFileCommand(documentIndexingService),
    new IndexDocsCommand(documentIndexingService),
    new DocChunksCommand(),
    new AskFileCommand(documentIndexingService, documentRetrievalService, documentQuestionAnsweringService),
    new AskDocsCommand(documentRetrievalService, documentQuestionAnsweringService),
    new SummarizeFileCommand(documentIndexingService, documentRetrievalService, documentSummaryService),
    new SummarizeDocsCommand(documentRetrievalService, documentSummaryService),
    new EmbedFileCommand(documentIndexingService, documentEmbeddingService),
    new EmbedDocsCommand(documentIndexingService, documentEmbeddingService),
    new ToolsCommand(toolRegistry),
    new HarnessesCommand(),
    new KillProcessCommand(pendingActionService, settings),
    new ActionCommand(pendingActionService, settings),
    new PendingCommand(pendingActionService, settings),
    new ApproveCommand(pendingActionService, pendingActionExecutor, settings),
    new DenyCommand(pendingActionService, settings),
    new PlanCommand(agentTaskService),
    new TasksCommand(agentTaskService),
    new TaskCommand(agentTaskService),
    new DoneCommand(agentTaskService),
    new CancelCommand(agentTaskService)
]);

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

if (settings.ApplyMigrations)
{
    await using var startupDb = new TelegramDbContext();
    await startupDb.Database.MigrateAsync(cts.Token);
}

ReceiverOptions receiverOptions = new()
{
    AllowedUpdates = Array.Empty<UpdateType>()
};

try
{
    var me = await botClient.GetMe(cts.Token);
    Console.WriteLine(AgentConsoleRenderer.RenderStartupPanel(new AgentConsoleSnapshot(
        BotUsername: me.Username ?? "unknown",
        OllamaUrl: settings.OllamaUrl,
        OllamaModel: settings.OllamaModel,
        DatabaseConnection: settings.DatabaseConnectionString,
        AccessMode: BotAccessPolicy.DescribeAccessMode(settings.AllowedChatIds, settings.AdminChatId, settings.AllowPublicAccess),
        MessageContentLoggingEnabled: settings.LogMessageContent,
        OnlineSearchEnabled: settings.EnableOnlineSearch,
        ApplyMigrations: settings.ApplyMigrations,
        Commands: commandRouter.Commands.Select(x => x.Name).ToList(),
        Tools: toolRegistry.Tools.Select(x => x.Name).ToList())));

    botClient.StartReceiving(
        updateHandler: HandleUpdateAsync,
        errorHandler: HandleErrorAsync,
        receiverOptions: receiverOptions,
        cancellationToken: cts.Token
    );

    WriteConsoleEvent("START", me.Username ?? "bot", "long polling is running", ConsoleEventLevel.Success);
    WriteConsoleEvent("CONSOLE", "local", "type a message or command here; use /exit to stop", ConsoleEventLevel.Info);

    _ = RunConsoleInputLoopAsync(cts.Token);
    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
    }
    catch (OperationCanceledException)
    {
        // Graceful shutdown requested from console, Ctrl+C, or host termination.
    }

    WriteConsoleEvent("STOP", "system", "shutdown requested", ConsoleEventLevel.Warning);
    cts.Cancel();
}
catch (ApiRequestException apiEx)
{
    SystemLogging.Instance.Log(
        0,
        "System",
        "Telegram API error occurred",
        $"[{apiEx.ErrorCode}] {apiEx.Message}",
        LogType.Error);

    Console.WriteLine($"Telegram API Error: [{apiEx.ErrorCode}] {apiEx.Message}");
}
catch (Exception ex)
{
    SystemLogging.Instance.Log(
        0,
        "System",
        "Unexpected error occurred",
        ex.Message,
        LogType.Error);

    Console.WriteLine($"Unexpected Error: {ex.Message}");
}

async Task RunConsoleInputLoopAsync(CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        Console.Write("> ");
        string? line = await Task.Run(Console.ReadLine, cancellationToken);
        if (line is null)
        {
            // In Windows Startup/background launchers stdin can be closed.
            // Keep Telegram long polling alive instead of shutting down the bot.
            WriteConsoleEvent("CONSOLE", "local", "stdin is closed; Telegram bot continues without console input", ConsoleEventLevel.Warning);
            return;
        }

        string input = line.Trim();
        if (input.Length == 0)
        {
            continue;
        }

        if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase)
            || input.Equals("exit", StringComparison.OrdinalIgnoreCase)
            || input.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            cts.Cancel();
            return;
        }

        try
        {
            string answer = await ProcessConsoleInputAsync(input, cancellationToken);
            Console.WriteLine();
            Console.WriteLine(answer);
            Console.WriteLine();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            WriteConsoleEvent("ERROR", "console", ex.Message, ConsoleEventLevel.Error);
        }
    }
}

async Task<string> ProcessConsoleInputAsync(string input, CancellationToken cancellationToken)
{
    await using TelegramDbContext dbContext = new();
    ConnectedUser consoleUser = await GetOrCreateConsoleUserAsync(dbContext, cancellationToken);

    var consoleMessage = new Message
    {
        Text = input,
        Chat = new Chat
        {
            Id = consoleUser.ChatId,
            Username = "local_console",
            FirstName = "Local",
            LastName = "Console"
        }
    };

    CommandResult commandResult = await commandRouter.TryHandleAsync(consoleMessage, consoleUser, dbContext, cancellationToken);
    if (commandResult.Handled)
    {
        WriteConsoleEvent("COMMAND", "console", input.Split(' ', 2)[0], ConsoleEventLevel.Success);
        return commandResult.ReplyText ?? "Command completed.";
    }

    dbContext.Messages.Add(new ChatMessage
    {
        ConnectedUserId = consoleUser.Id,
        ChatId = consoleUser.ChatId,
        Content = input,
        Role = ChatRoles.User,
        Timestamp = DateTime.UtcNow
    });
    await dbContext.SaveChangesAsync(cancellationToken);

    List<OllamaMessageDto> conversationContext = await conversationService.CreateConversationContextAsync(
        dbContext,
        consoleUser.Id,
        maxHistory: 8,
        cancellationToken: cancellationToken,
        toolInstructions: toolRegistry.RenderToolInstructions());

    string finalAnswer = await agentRunner.RunAsync(conversationContext, cancellationToken);
    WriteConsoleEvent("MESSAGE", "console", $"answered {finalAnswer.Length} chars", ConsoleEventLevel.Success);

    dbContext.Messages.Add(new ChatMessage
    {
        ConnectedUserId = consoleUser.Id,
        ChatId = consoleUser.ChatId,
        Content = finalAnswer,
        Role = ChatRoles.Assistant,
        Timestamp = DateTime.UtcNow
    });
    await dbContext.SaveChangesAsync(cancellationToken);

    return finalAnswer;
}

async Task<ConnectedUser> GetOrCreateConsoleUserAsync(TelegramDbContext dbContext, CancellationToken cancellationToken)
{
    const long consoleChatId = 0;
    ConnectedUser? user = await dbContext.Users.FirstOrDefaultAsync(x => x.ChatId == consoleChatId, cancellationToken);
    if (user is not null)
    {
        user.Name = "local_console";
        user.FirstName = "Local";
        user.LastName = "Console";
        user.LastSeenAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    user = new ConnectedUser
    {
        ChatId = consoleChatId,
        Name = "local_console",
        FirstName = "Local",
        LastName = "Console",
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow
    };
    dbContext.Users.Add(user);
    await dbContext.SaveChangesAsync(cancellationToken);
    return user;
}

async Task HandleUpdateAsync(
    ITelegramBotClient bot,
    Update update,
    CancellationToken cancellationToken)
{
    if (update.Message is not { } message)
    {
        return;
    }

    if (message.Text is null && message.Document is null)
    {
        return;
    }

    string messageText = message.Text ?? string.Empty;

    try
    {
        if (!BotAccessPolicy.IsAllowed(
                message.Chat.Id,
                settings.AllowedChatIds,
                settings.AdminChatId,
                settings.AllowPublicAccess))
        {
            WriteConsoleEvent("DENIED", message.Chat.Username ?? message.Chat.Id.ToString(), "chat ID is not allowed", ConsoleEventLevel.Warning);

            SystemLogging.Instance.Log(
                message.Chat.Id,
                message.Chat.Username ?? "Unknown",
                "Access denied",
                "Chat ID is not allowed by current access mode",
                LogType.Warning);

            await bot.SendMessage(
                chatId: message.Chat.Id,
                text: BotAccessPolicy.AccessDeniedMessage(
                    settings.AllowPublicAccess,
                    settings.AllowedChatIds,
                    settings.AdminChatId),
                cancellationToken: cancellationToken);

            if (settings.AdminChatId > 0)
            {
                await bot.SendMessage(
                    chatId: settings.AdminChatId,
                    text: $"Bot Alert: blocked chat ID {message.Chat.Id} tried to use the bot.",
                    cancellationToken: cancellationToken);
            }

            return;
        }

        await using TelegramDbContext dbContext = new();

        ConnectedUser? user = await dbContext.Users
            .FirstOrDefaultAsync(x => x.ChatId == message.Chat.Id, cancellationToken);

        bool isNewUser = false;

        if (user is null)
        {
            isNewUser = true;

            user = new ConnectedUser
            {
                ChatId = message.Chat.Id,
                Name = message.Chat.Username ?? string.Empty,
                FirstName = message.Chat.FirstName ?? string.Empty,
                LastName = message.Chat.LastName ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };

            dbContext.Users.Add(user);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                dbContext.ChangeTracker.Clear();
                user = await dbContext.Users
                    .FirstAsync(x => x.ChatId == message.Chat.Id, cancellationToken);
                isNewUser = false;
            }
        }
        else
        {
            user.Name = message.Chat.Username ?? string.Empty;
            user.FirstName = message.Chat.FirstName ?? string.Empty;
            user.LastName = message.Chat.LastName ?? string.Empty;
            user.LastSeenAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (isNewUser && settings.AdminChatId > 0)
        {
            WriteConsoleEvent("USER", message.Chat.Username ?? message.Chat.Id.ToString(), "new user connected", ConsoleEventLevel.Info);

            await bot.SendMessage(
                chatId: settings.AdminChatId,
                text: "Bot Alert: New user connected\nInfo:\n" + user,
                cancellationToken: cancellationToken
            );
        }

        if (message.Document is not null)
        {
            await HandleDocumentAsync(bot, message, user, dbContext, cancellationToken);
            return;
        }

        CommandResult commandResult = await commandRouter.TryHandleAsync(message, user, dbContext, cancellationToken);
        if (commandResult.Handled)
        {
            WriteConsoleEvent("COMMAND", message.Chat.Username ?? message.Chat.Id.ToString(), (message.Text ?? string.Empty).Split(' ', 2)[0], ConsoleEventLevel.Success);

            if (!string.IsNullOrWhiteSpace(commandResult.ReplyText))
            {
                foreach (string replyChunk in TelegramMessageFormatter.SplitForTelegram(commandResult.ReplyText))
                {
                    await bot.SendMessage(
                        chatId: message.Chat.Id,
                        text: replyChunk,
                        replyParameters: new ReplyParameters
                        {
                            MessageId = message.MessageId
                        },
                        cancellationToken: cancellationToken);
                }
            }

            return;
        }

        dbContext.Messages.Add(new ChatMessage
        {
            ConnectedUserId = user.Id,
            ChatId = message.Chat.Id,
            Content = messageText,
            Role = ChatRoles.User,
            Timestamp = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        List<OllamaMessageDto> conversationContext =
            await conversationService.CreateConversationContextAsync(
                dbContext,
                user.Id,
                maxHistory: 8,
                cancellationToken: cancellationToken,
                toolInstructions: toolRegistry.RenderToolInstructions());

        string finalAnswer = await agentRunner.RunAsync(conversationContext, cancellationToken);
        WriteConsoleEvent("MESSAGE", message.Chat.Username ?? message.Chat.Id.ToString(), $"answered {finalAnswer.Length} chars", ConsoleEventLevel.Success);

        dbContext.Messages.Add(new ChatMessage
        {
            ConnectedUserId = user.Id,
            ChatId = message.Chat.Id,
            Content = finalAnswer,
            Role = ChatRoles.Assistant,
            Timestamp = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        SystemLogging.Instance.Log(
            message.Chat.Id,
            message.Chat.Username ?? "Unknown",
            settings.LogMessageContent ? messageText : "[message content logging disabled]",
            settings.LogMessageContent ? finalAnswer : "[response content logging disabled]",
            LogType.Info);

        foreach (string replyChunk in TelegramMessageFormatter.SplitForTelegram(finalAnswer))
        {
            await bot.SendMessage(
                chatId: message.Chat.Id,
                text: replyChunk,
                replyParameters: new ReplyParameters
                {
                    MessageId = message.MessageId
                },
                cancellationToken: cancellationToken
            );
        }
    }
    catch (Exception ex)
    {
        WriteConsoleEvent("ERROR", message.Chat.Username ?? message.Chat.Id.ToString(), ex.Message, ConsoleEventLevel.Error);

        SystemLogging.Instance.Log(
            message.Chat.Id,
            message.Chat.Username ?? "Unknown",
            settings.LogMessageContent ? messageText : "[message content logging disabled]",
            ex.Message,
            LogType.Error);

        Console.WriteLine($"Error processing update: {ex}");

        await bot.SendMessage(
            chatId: message.Chat.Id,
            text: "Sorry, an error happened while processing your message.",
            cancellationToken: cancellationToken
        );
    }
}

async Task HandleDocumentAsync(
    ITelegramBotClient bot,
    Message message,
    ConnectedUser user,
    TelegramDbContext dbContext,
    CancellationToken cancellationToken)
{
    const long telegramBotDownloadLimitBytes = 25L * 1024 * 1024;

    if (message.Document is null)
    {
        return;
    }

    string fileName = message.Document.FileName ?? "document.txt";
    if (!documentStorage.IsAllowedFileName(fileName))
    {
        await bot.SendMessage(
            chatId: message.Chat.Id,
            text: $"Unsupported document type. Please upload one of: {documentStorage.AllowedExtensionsText}.",
            replyParameters: new ReplyParameters { MessageId = message.MessageId },
            cancellationToken: cancellationToken);
        return;
    }

    if (message.Document.FileSize > telegramBotDownloadLimitBytes)
    {
        await bot.SendMessage(
            chatId: message.Chat.Id,
            text: $"Document is too large for the standard Telegram Bot API download limit. Maximum: {LocalDeviceInfoService.FormatBytes(telegramBotDownloadLimitBytes)}. Please compress it, split it, or upload a smaller/exported text/PDF/DOCX/XLSX file.",
            replyParameters: new ReplyParameters { MessageId = message.MessageId },
            cancellationToken: cancellationToken);
        return;
    }

    try
    {
        var telegramFile = await bot.GetFile(message.Document.FileId, cancellationToken);
        if (string.IsNullOrWhiteSpace(telegramFile.FilePath))
        {
            await bot.SendMessage(
                chatId: message.Chat.Id,
                text: "Telegram did not provide a downloadable file path for this document. The bot now allows up to 25 MB, but Telegram may still refuse some files above about 20 MB or files whose downloadable size could not be verified. Please compress it, split it, or place it in ImportInbox and use /importfile <filename>.",
                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                cancellationToken: cancellationToken);
            return;
        }

        await using var stream = new MemoryStream();
        await bot.DownloadFile(telegramFile.FilePath, stream, cancellationToken);
        stream.Position = 0;

        UploadedFile savedFile = await documentStorage.SaveUploadedFileAsync(
            user,
            fileName,
            message.Document.FileId,
            message.Document.MimeType ?? string.Empty,
            stream,
            message.Document.FileSize,
            cancellationToken);

        dbContext.UploadedFiles.Add(savedFile);
        await dbContext.SaveChangesAsync(cancellationToken);

        WriteConsoleEvent("DOCUMENT", message.Chat.Username ?? message.Chat.Id.ToString(), $"saved {savedFile.OriginalFileName} as #{savedFile.Id}", ConsoleEventLevel.Success);

        await bot.SendMessage(
            chatId: message.Chat.Id,
            text: $"File saved as #{savedFile.Id}: {savedFile.OriginalFileName}\nUse /readfile {savedFile.Id} for documents, /images for image files, or /files to list saved files.",
            replyParameters: new ReplyParameters { MessageId = message.MessageId },
            cancellationToken: cancellationToken);
    }
    catch (ApiRequestException ex) when (ex.Message.Contains("too large", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("file is too big", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("file_id", StringComparison.OrdinalIgnoreCase))
    {
        await bot.SendMessage(
            chatId: message.Chat.Id,
            text: "Telegram refused to provide this document through the standard Bot API. Some Telegram Bot API deployments still refuse downloads above about 20 MB even when this bot allows up to 25 MB. Please compress it, split it, or place it in ImportInbox and use /importfile <filename>.",
            replyParameters: new ReplyParameters { MessageId = message.MessageId },
            cancellationToken: cancellationToken);
    }
    catch (InvalidOperationException ex)
    {
        await bot.SendMessage(
            chatId: message.Chat.Id,
            text: ex.Message,
            replyParameters: new ReplyParameters { MessageId = message.MessageId },
            cancellationToken: cancellationToken);
    }
}

Task HandleErrorAsync(
    ITelegramBotClient bot,
    Exception exception,
    CancellationToken cancellationToken)
{
    string errorMessage = TelegramReceiverErrorClassifier.Summarize(exception);
    bool isTransient = TelegramReceiverErrorClassifier.IsTransientNetworkError(exception);

    WriteConsoleEvent(
        isTransient ? "NET" : "ERROR",
        "telegram",
        errorMessage,
        isTransient ? ConsoleEventLevel.Warning : ConsoleEventLevel.Error);

    SystemLogging.Instance.Log(
        0,
        "System",
        isTransient ? "Transient Telegram receiver network error" : "Telegram receiver error",
        errorMessage,
        isTransient ? LogType.Warning : LogType.Error);

    if (!isTransient)
    {
        Console.WriteLine(exception);
    }

    return Task.CompletedTask;
}
void WriteConsoleEvent(string label, string actor, string detail, ConsoleEventLevel level)
{
    ConsoleColor originalColor = Console.ForegroundColor;
    Console.ForegroundColor = level switch
    {
        ConsoleEventLevel.Success => ConsoleColor.Green,
        ConsoleEventLevel.Warning => ConsoleColor.Yellow,
        ConsoleEventLevel.Error => ConsoleColor.Red,
        _ => ConsoleColor.Cyan
    };

    Console.WriteLine(AgentConsoleRenderer.RenderEvent(label, actor, detail, level));
    Console.ForegroundColor = originalColor;
}
