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
var toolRegistry = new ToolRegistry([
    new DateTimeTool(),
    new CalculatorTool(),
    new BotStatusTool(settings),
    new OnlineSearchTool(searchClient)
]);
var documentStorage = new DocumentStorageService(Path.Combine(AppContext.BaseDirectory, "UserFiles"));
var pendingActionService = new PendingActionService();
var agentRunner = new AgentRunner(ollamaClient, toolRegistry);
var conversationService = new ConversationService();
var commandRouter = new CommandRouter([
    new HelpCommand(),
    new StatusCommand(settings),
    new ResetCommand(),
    new RememberCommand(),
    new MemoryCommand(),
    new ForgetCommand(),
    new FilesCommand(documentStorage),
    new ReadFileCommand(documentStorage),
    new CreateFileCommand(documentStorage),
    new ToolsCommand(toolRegistry),
    new PendingCommand(pendingActionService),
    new ApproveCommand(pendingActionService),
    new DenyCommand(pendingActionService)
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
        AllowlistEnabled: settings.AllowedChatIds.Count > 0,
        MessageContentLoggingEnabled: settings.LogMessageContent,
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
    Console.ReadLine();
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
        if (!BotAccessPolicy.IsAllowed(message.Chat.Id, settings.AllowedChatIds))
        {
            WriteConsoleEvent("DENIED", message.Chat.Username ?? message.Chat.Id.ToString(), "chat ID is not allowed", ConsoleEventLevel.Warning);

            SystemLogging.Instance.Log(
                message.Chat.Id,
                message.Chat.Username ?? "Unknown",
                "Access denied",
                "Chat ID is not in ALLOWED_CHAT_IDS",
                LogType.Warning);

            await bot.SendMessage(
                chatId: message.Chat.Id,
                text: "Access denied. Ask the bot administrator to add your chat ID.",
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

    try
    {
        var telegramFile = await bot.GetFile(message.Document.FileId, cancellationToken);
        await using var stream = new MemoryStream();
        await bot.DownloadFile(telegramFile.FilePath!, stream, cancellationToken);
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
            text: $"Document saved as #{savedFile.Id}: {savedFile.OriginalFileName}\nUse /readfile {savedFile.Id} to read it or /files to list saved files.",
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
