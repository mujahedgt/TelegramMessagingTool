using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Commands;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.models;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Telegram;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.Runtime;

public sealed class TelegramUpdateHandler
{
    private readonly BotSettings _settings;
    private readonly DocumentStorageService _documentStorage;
    private readonly ToolRegistry _toolRegistry;
    private readonly PendingActionCallbackService _pendingActionCallbackService;
    private readonly TaskCallbackService _taskCallbackService;
    private readonly AgentRunner _agentRunner;
    private readonly ConversationService _conversationService;
    private readonly CommandRouter _commandRouter;
    private readonly Action<string, string, string, ConsoleEventLevel> _writeConsoleEvent;

    public TelegramUpdateHandler(
        BotSettings settings,
        DocumentStorageService documentStorage,
        ToolRegistry toolRegistry,
        PendingActionCallbackService pendingActionCallbackService,
        TaskCallbackService taskCallbackService,
        AgentRunner agentRunner,
        ConversationService conversationService,
        CommandRouter commandRouter,
        Action<string, string, string, ConsoleEventLevel> writeConsoleEvent)
    {
        _settings = settings;
        _documentStorage = documentStorage;
        _toolRegistry = toolRegistry;
        _pendingActionCallbackService = pendingActionCallbackService;
        _taskCallbackService = taskCallbackService;
        _agentRunner = agentRunner;
        _conversationService = conversationService;
        _commandRouter = commandRouter;
        _writeConsoleEvent = writeConsoleEvent;
    }

    public async Task HandleUpdateAsync(
        ITelegramBotClient bot,
        Update update,
        CancellationToken cancellationToken)
    {
        if (update.CallbackQuery is { } callbackQuery)
        {
            await HandleCallbackQueryAsync(bot, callbackQuery, cancellationToken);
            return;
        }

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
                    _settings.AllowedChatIds,
                    _settings.AdminChatId,
                    _settings.AllowPublicAccess))
            {
                _writeConsoleEvent("DENIED", message.Chat.Username ?? message.Chat.Id.ToString(), "chat ID is not allowed", ConsoleEventLevel.Warning);

                SystemLogging.Instance.Log(
                    message.Chat.Id,
                    message.Chat.Username ?? "Unknown",
                    "Access denied",
                    "Chat ID is not allowed by current access mode",
                    LogType.Warning);

                await bot.SendMessage(
                    chatId: message.Chat.Id,
                    text: BotAccessPolicy.AccessDeniedMessage(
                        _settings.AllowPublicAccess,
                        _settings.AllowedChatIds,
                        _settings.AdminChatId),
                    cancellationToken: cancellationToken);

                if (_settings.AdminChatId > 0)
                {
                    await bot.SendMessage(
                        chatId: _settings.AdminChatId,
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

            if (isNewUser && _settings.AdminChatId > 0)
            {
                _writeConsoleEvent("USER", message.Chat.Username ?? message.Chat.Id.ToString(), "new user connected", ConsoleEventLevel.Info);

                await bot.SendMessage(
                    chatId: _settings.AdminChatId,
                    text: "Bot Alert: New user connected\nInfo:\n" + user,
                    cancellationToken: cancellationToken
                );
            }

            if (message.Document is not null)
            {
                await HandleDocumentAsync(bot, message, user, dbContext, cancellationToken);
                return;
            }

            CommandResult commandResult = await _commandRouter.TryHandleAsync(message, user, dbContext, cancellationToken);
            if (commandResult.Handled)
            {
                _writeConsoleEvent("COMMAND", message.Chat.Username ?? message.Chat.Id.ToString(), (message.Text ?? string.Empty).Split(' ', 2)[0], ConsoleEventLevel.Success);

                if (!string.IsNullOrWhiteSpace(commandResult.ReplyText))
                {
                    List<string> replyChunks = TelegramMessageFormatter.SplitForTelegram(commandResult.ReplyText).ToList();
                    for (int index = 0; index < replyChunks.Count; index++)
                    {
                        await bot.SendMessage(
                            chatId: message.Chat.Id,
                            text: replyChunks[index],
                            replyParameters: new ReplyParameters
                            {
                                MessageId = message.MessageId
                            },
                            replyMarkup: index == 0 ? commandResult.ReplyMarkup : null,
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
                await _conversationService.CreateConversationContextAsync(
                    dbContext,
                    user.Id,
                    maxHistory: _settings.ConversationMaxHistory,
                    cancellationToken: cancellationToken,
                    toolInstructions: _toolRegistry.RenderToolInstructions());

            string finalAnswer = await _agentRunner.RunAsync(
                conversationContext,
                cancellationToken,
                dbContext,
                user);
            _writeConsoleEvent("MESSAGE", message.Chat.Username ?? message.Chat.Id.ToString(), $"answered {finalAnswer.Length} chars", ConsoleEventLevel.Success);

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
                _settings.LogMessageContent ? messageText : "[message content logging disabled]",
                _settings.LogMessageContent ? finalAnswer : "[response content logging disabled]",
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
            _writeConsoleEvent("ERROR", message.Chat.Username ?? message.Chat.Id.ToString(), ex.Message, ConsoleEventLevel.Error);

            SystemLogging.Instance.Log(
                message.Chat.Id,
                message.Chat.Username ?? "Unknown",
                _settings.LogMessageContent ? messageText : "[message content logging disabled]",
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

    private async Task HandleCallbackQueryAsync(
        ITelegramBotClient bot,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        if (callbackQuery.Message is not { } callbackMessage)
        {
            await bot.AnswerCallbackQuery(
                callbackQuery.Id,
                text: "This button can no longer be handled.",
                cancellationToken: cancellationToken);
            return;
        }

        long chatId = callbackMessage.Chat.Id;
        string actor = callbackQuery.From.Username ?? callbackQuery.From.Id.ToString();

        try
        {
            if (!BotAccessPolicy.IsAllowed(chatId, _settings.AllowedChatIds, _settings.AdminChatId, _settings.AllowPublicAccess))
            {
                _writeConsoleEvent("DENIED", actor, "callback chat ID is not allowed", ConsoleEventLevel.Warning);
                await bot.AnswerCallbackQuery(
                    callbackQuery.Id,
                    text: "Access denied",
                    showAlert: true,
                    cancellationToken: cancellationToken);
                return;
            }

            await using TelegramDbContext dbContext = new();
            ConnectedUser? user = await dbContext.Users.FirstOrDefaultAsync(x => x.ChatId == chatId, cancellationToken);
            if (user is null)
            {
                user = new ConnectedUser
                {
                    ChatId = chatId,
                    Name = callbackQuery.From.Username ?? string.Empty,
                    FirstName = callbackQuery.From.FirstName,
                    LastName = callbackQuery.From.LastName ?? string.Empty,
                    CreatedAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                };
                dbContext.Users.Add(user);
            }
            else
            {
                user.Name = callbackQuery.From.Username ?? string.Empty;
                user.FirstName = callbackQuery.From.FirstName;
                user.LastName = callbackQuery.From.LastName ?? string.Empty;
                user.LastSeenAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            PendingActionCallbackResult pendingActionResult = await _pendingActionCallbackService.HandleAsync(
                callbackQuery.Data,
                user,
                dbContext,
                cancellationToken);

            bool handled = pendingActionResult.Handled;
            string answerText = pendingActionResult.AnswerText;
            string? messageText = pendingActionResult.MessageText;

            if (!handled)
            {
                TaskCallbackResult taskResult = await _taskCallbackService.HandleAsync(
                    callbackQuery.Data,
                    user,
                    dbContext,
                    cancellationToken);

                handled = taskResult.Handled;
                answerText = taskResult.AnswerText;
                messageText = taskResult.MessageText;
            }

            await bot.AnswerCallbackQuery(
                callbackQuery.Id,
                text: answerText,
                showAlert: false,
                cancellationToken: cancellationToken);

            if (!handled)
            {
                _writeConsoleEvent("CALLBACK", actor, "unsupported callback data", ConsoleEventLevel.Warning);
                return;
            }

            _writeConsoleEvent("CALLBACK", actor, callbackQuery.Data ?? "empty", ConsoleEventLevel.Success);

            if (!string.IsNullOrWhiteSpace(messageText))
            {
                foreach (string replyChunk in TelegramMessageFormatter.SplitForTelegram(messageText))
                {
                    await bot.SendMessage(
                        chatId: chatId,
                        text: replyChunk,
                        replyParameters: new ReplyParameters { MessageId = callbackMessage.MessageId },
                        cancellationToken: cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _writeConsoleEvent("ERROR", actor, ex.Message, ConsoleEventLevel.Error);
            await bot.AnswerCallbackQuery(
                callbackQuery.Id,
                text: "Sorry, that button action failed.",
                showAlert: true,
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleDocumentAsync(
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
        if (!_documentStorage.IsAllowedFileName(fileName))
        {
            await bot.SendMessage(
                chatId: message.Chat.Id,
                text: $"Unsupported document type. Please upload one of: {_documentStorage.AllowedExtensionsText}.",
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

            UploadedFile savedFile = await _documentStorage.SaveUploadedFileAsync(
                user,
                fileName,
                message.Document.FileId,
                message.Document.MimeType ?? string.Empty,
                stream,
                message.Document.FileSize,
                cancellationToken);

            dbContext.UploadedFiles.Add(savedFile);
            await dbContext.SaveChangesAsync(cancellationToken);

            _writeConsoleEvent("DOCUMENT", message.Chat.Username ?? message.Chat.Id.ToString(), $"saved {savedFile.OriginalFileName} as #{savedFile.Id}", ConsoleEventLevel.Success);

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

    public Task HandleErrorAsync(
        ITelegramBotClient bot,
        Exception exception,
        CancellationToken cancellationToken)
    {
        string errorMessage = TelegramReceiverErrorClassifier.Summarize(exception);
        bool isTransient = TelegramReceiverErrorClassifier.IsTransientNetworkError(exception);

        _writeConsoleEvent(
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
}
