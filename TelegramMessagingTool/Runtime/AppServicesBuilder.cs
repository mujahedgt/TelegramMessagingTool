using System.Net;
using Telegram.Bot;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Telegram;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.Runtime;

public static class AppServicesBuilder
{
    public static AppServices Build(
        BotSettings settings,
        Action<string, string, string, ConsoleEventLevel>? writeConsoleEvent = null,
        Action? requestShutdown = null)
    {
        writeConsoleEvent ??= static (_, _, _, _) => { };
        requestShutdown ??= static () => { };
        var qwenClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        var embeddingClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        var searchClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        var telegramHttpHandler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(30)
        };

        var telegramHttpClient = new HttpClient(telegramHttpHandler)
        {
            Timeout = TimeSpan.FromSeconds(90)
        };

        var botClient = new TelegramBotClient(settings.BotToken, telegramHttpClient, CancellationToken.None);
        var taskReminderService = new TaskReminderService(new TelegramTaskReminderSender(botClient));
        var ollamaClient = new OllamaChatClient(qwenClient, settings);
        var ollamaEmbeddingClient = new OllamaEmbeddingClient(embeddingClient, settings);
        ITextEmbeddingService? retrievalEmbeddingService = settings.EnableDocumentEmbeddings ? ollamaEmbeddingClient : null;
        var documentStorage = new DocumentStorageService(Path.Combine(Environment.CurrentDirectory, "UserFiles"));
        string importDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "ImportInbox"));
        var pendingActionService = new PendingActionService();
        var toolRegistry = ToolRegistryFactory.Create(settings, searchClient, pendingActionService);
        ISearchRoutingClassifier searchRoutingClassifier = SearchRoutingClassifierFactory.Create(settings.SearchRoutingMode, ollamaClient);
        var pendingActionExecutor = new PendingActionExecutor(new SystemProcessTerminator(), documentStorage);
        var pendingActionCallbackService = new PendingActionCallbackService(pendingActionService, pendingActionExecutor, settings);
        var agentTaskService = new AgentTaskService();
        var taskCallbackService = new TaskCallbackService(agentTaskService);
        var documentIndexingService = new DocumentIndexingService(documentStorage);
        var documentEmbeddingService = new DocumentEmbeddingService(ollamaEmbeddingClient, settings.OllamaEmbeddingModel);
        var documentRetrievalService = new DocumentRetrievalService(retrievalEmbeddingService);
        var documentQuestionAnsweringService = new DocumentQuestionAnsweringService(ollamaClient);
        var documentSummaryService = new DocumentSummaryService(ollamaClient);
        var transcriptInsightsService = new TranscriptInsightsService(ollamaClient);
        var imageDescriptionService = new OllamaImageDescriptionService(qwenClient, settings);
        IAudioTranscriptionService? audioTranscriptionService = string.IsNullOrWhiteSpace(settings.AudioTranscriptionCommand)
            ? null
            : new LocalCommandAudioTranscriptionService(
                settings.AudioTranscriptionCommand,
                settings.AudioTranscriptionArguments,
                TimeSpan.FromSeconds(settings.AudioTranscriptionTimeoutSeconds));
        var agentRunner = new AgentRunner(ollamaClient, toolRegistry, searchRoutingClassifier: searchRoutingClassifier);
        var conversationService = new ConversationService();
        var commandRouter = CommandRouterFactory.Create(
            settings,
            toolRegistry,
            documentStorage,
            importDirectory,
            pendingActionService,
            pendingActionExecutor,
            agentTaskService,
            documentIndexingService,
            documentRetrievalService,
            documentQuestionAnsweringService,
            documentSummaryService,
            transcriptInsightsService,
            documentEmbeddingService,
            imageDescriptionService,
            audioTranscriptionService);
        var telegramUpdateHandler = new TelegramUpdateHandler(
            settings,
            documentStorage,
            toolRegistry,
            pendingActionCallbackService,
            taskCallbackService,
            agentRunner,
            conversationService,
            commandRouter,
            writeConsoleEvent);
        var consoleInputHandler = new ConsoleInputHandler(
            settings,
            toolRegistry,
            agentRunner,
            conversationService,
            commandRouter,
            writeConsoleEvent,
            requestShutdown);
        var taskReminderLoop = new TaskReminderLoop(taskReminderService, writeConsoleEvent);

        return new AppServices(
            [qwenClient, embeddingClient, searchClient, telegramHttpHandler, telegramHttpClient],
            botClient,
            taskReminderService,
            ollamaClient,
            ollamaEmbeddingClient,
            documentStorage,
            importDirectory,
            pendingActionService,
            toolRegistry,
            searchRoutingClassifier,
            pendingActionExecutor,
            pendingActionCallbackService,
            agentTaskService,
            taskCallbackService,
            documentIndexingService,
            documentEmbeddingService,
            documentRetrievalService,
            documentQuestionAnsweringService,
            documentSummaryService,
            imageDescriptionService,
            agentRunner,
            conversationService,
            commandRouter,
            telegramUpdateHandler,
            consoleInputHandler,
            taskReminderLoop);
    }
}
