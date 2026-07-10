using System.Net;
using Telegram.Bot;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Services.Vector;
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

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var botClient = new TelegramBotClient(settings.BotToken, telegramHttpClient, CancellationToken.None);
        var runtimeEventBuffer = new RuntimeEventBuffer();
        var runtimeDashboardService = new RuntimeDashboardService(settings, runtimeEventBuffer, startedAt);
        void BufferedConsoleEvent(string label, string actor, string detail, ConsoleEventLevel level)
        {
            runtimeEventBuffer.Record(level, label, $"{actor}: {detail}");
            writeConsoleEvent(label, actor, detail, level);
        }

        var taskReminderService = new TaskReminderService(new TelegramTaskReminderSender(botClient));
        var ollamaClient = new OllamaChatClient(qwenClient, settings);
        var ollamaEmbeddingClient = new OllamaEmbeddingClient(embeddingClient, settings);
        VectorStoreFactoryResult vectorStoreFactoryResult = VectorStoreFactory.Create(
            settings.VectorStoreProvider,
            settings.VectorStorePath,
            settings.QdrantUrl,
            settings.QdrantCollection,
            embeddingClient);
        IVectorStore? vectorStore = vectorStoreFactoryResult.VectorStore;
        ITextEmbeddingService? retrievalEmbeddingService = settings.EnableDocumentEmbeddings ? ollamaEmbeddingClient : null;
        var documentStorage = new DocumentStorageService(Path.Combine(Environment.CurrentDirectory, "UserFiles"));
        string importDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "ImportInbox"));
        var observability = new RuntimeObservabilityService(detail => BufferedConsoleEvent("OBSERVE", "runtime", detail, ConsoleEventLevel.Info));
        var pendingActionService = new PendingActionService(observability);
        var toolRegistry = ToolRegistryFactory.Create(settings, searchClient, pendingActionService);
        ISearchRoutingClassifier searchRoutingClassifier = SearchRoutingClassifierFactory.Create(settings.SearchRoutingMode, ollamaClient);
        var pendingActionExecutor = new PendingActionExecutor(new SystemProcessTerminator(), documentStorage, observability: observability);
        var pendingActionCallbackService = new PendingActionCallbackService(pendingActionService, pendingActionExecutor, settings, observability);
        var agentTaskService = new AgentTaskService();
        var taskCallbackService = new TaskCallbackService(agentTaskService, settings, observability);
        var documentIndexingService = new DocumentIndexingService(documentStorage);
        var documentEmbeddingService = new DocumentEmbeddingService(ollamaEmbeddingClient, settings.OllamaEmbeddingModel, vectorStore);
        var vectorMaintenanceService = new VectorMaintenanceService(documentIndexingService, documentEmbeddingService, vectorStore);
        var documentRetrievalService = new DocumentRetrievalService(retrievalEmbeddingService, vectorStore);
        var documentQuestionAnsweringService = new DocumentQuestionAnsweringService(ollamaClient);
        var documentSummaryService = new DocumentSummaryService(ollamaClient);
        var transcriptInsightsService = new TranscriptInsightsService(ollamaClient);
        var imagePromptService = new ImagePromptService(ollamaClient);
        var imageDescriptionService = new OllamaImageDescriptionService(qwenClient, settings);
        IAudioTranscriptionService? audioTranscriptionService = string.IsNullOrWhiteSpace(settings.AudioTranscriptionCommand)
            ? null
            : new LocalCommandAudioTranscriptionService(
                settings.AudioTranscriptionCommand,
                settings.AudioTranscriptionArguments,
                TimeSpan.FromSeconds(settings.AudioTranscriptionTimeoutSeconds));
        ITextToSpeechService? textToSpeechService = string.IsNullOrWhiteSpace(settings.TextToSpeechCommand)
            ? null
            : new LocalCommandTextToSpeechService(
                settings.TextToSpeechCommand,
                settings.TextToSpeechArguments,
                TimeSpan.FromSeconds(settings.TextToSpeechTimeoutSeconds),
                settings.TextToSpeechOutputExtension);
        var agentRunner = new AgentRunner(
            ollamaClient,
            toolRegistry,
            searchRoutingClassifier: searchRoutingClassifier,
            observability: observability,
            streamingChatClient: ollamaClient);
        var conversationService = new ConversationService();
        var voiceMessageProcessor = new VoiceMessageProcessor(
            settings,
            documentStorage,
            toolRegistry,
            agentRunner,
            conversationService,
            audioTranscriptionService,
            textToSpeechService);
        var reactionService = new TelegramReactionService(BufferedConsoleEvent);
        var typingService = new TelegramTypingService(BufferedConsoleEvent);
        var streamEditService = new TelegramStreamEditService();
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
            imagePromptService,
            documentEmbeddingService,
            vectorMaintenanceService,
            imageDescriptionService,
            audioTranscriptionService,
            textToSpeechService,
            runtimeEventBuffer);
        var telegramUpdateHandler = new TelegramUpdateHandler(
            settings,
            documentStorage,
            toolRegistry,
            pendingActionCallbackService,
            taskCallbackService,
            agentRunner,
            conversationService,
            commandRouter,
            voiceMessageProcessor,
            reactionService,
            typingService,
            streamEditService,
            BufferedConsoleEvent);
        var consoleInputHandler = new ConsoleInputHandler(
            settings,
            toolRegistry,
            agentRunner,
            conversationService,
            commandRouter,
            runtimeDashboardService,
            runtimeEventBuffer,
            BufferedConsoleEvent,
            requestShutdown);
        var taskReminderLoop = new TaskReminderLoop(taskReminderService, BufferedConsoleEvent);

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
