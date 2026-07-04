using Telegram.Bot;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Commands;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Telegram;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.Runtime;

public sealed class AppServices : IDisposable
{
    private readonly IReadOnlyList<IDisposable> _disposables;

    public AppServices(
        IReadOnlyList<IDisposable> disposables,
        TelegramBotClient botClient,
        TaskReminderService taskReminderService,
        OllamaChatClient ollamaClient,
        OllamaEmbeddingClient ollamaEmbeddingClient,
        DocumentStorageService documentStorage,
        string importDirectory,
        PendingActionService pendingActionService,
        ToolRegistry toolRegistry,
        ISearchRoutingClassifier searchRoutingClassifier,
        PendingActionExecutor pendingActionExecutor,
        PendingActionCallbackService pendingActionCallbackService,
        AgentTaskService agentTaskService,
        TaskCallbackService taskCallbackService,
        DocumentIndexingService documentIndexingService,
        DocumentEmbeddingService documentEmbeddingService,
        DocumentRetrievalService documentRetrievalService,
        DocumentQuestionAnsweringService documentQuestionAnsweringService,
        DocumentSummaryService documentSummaryService,
        IImageDescriptionService imageDescriptionService,
        AgentRunner agentRunner,
        ConversationService conversationService,
        CommandRouter commandRouter,
        TelegramUpdateHandler telegramUpdateHandler,
        ConsoleInputHandler consoleInputHandler,
        TaskReminderLoop taskReminderLoop)
    {
        _disposables = disposables;
        BotClient = botClient;
        TaskReminderService = taskReminderService;
        OllamaClient = ollamaClient;
        OllamaEmbeddingClient = ollamaEmbeddingClient;
        DocumentStorage = documentStorage;
        ImportDirectory = importDirectory;
        PendingActionService = pendingActionService;
        ToolRegistry = toolRegistry;
        SearchRoutingClassifier = searchRoutingClassifier;
        PendingActionExecutor = pendingActionExecutor;
        PendingActionCallbackService = pendingActionCallbackService;
        AgentTaskService = agentTaskService;
        TaskCallbackService = taskCallbackService;
        DocumentIndexingService = documentIndexingService;
        DocumentEmbeddingService = documentEmbeddingService;
        DocumentRetrievalService = documentRetrievalService;
        DocumentQuestionAnsweringService = documentQuestionAnsweringService;
        DocumentSummaryService = documentSummaryService;
        ImageDescriptionService = imageDescriptionService;
        AgentRunner = agentRunner;
        ConversationService = conversationService;
        CommandRouter = commandRouter;
        TelegramUpdateHandler = telegramUpdateHandler;
        ConsoleInputHandler = consoleInputHandler;
        TaskReminderLoop = taskReminderLoop;
    }

    public TelegramBotClient BotClient { get; }

    public TaskReminderService TaskReminderService { get; }

    public OllamaChatClient OllamaClient { get; }

    public OllamaEmbeddingClient OllamaEmbeddingClient { get; }

    public DocumentStorageService DocumentStorage { get; }

    public string ImportDirectory { get; }

    public PendingActionService PendingActionService { get; }

    public ToolRegistry ToolRegistry { get; }

    public ISearchRoutingClassifier SearchRoutingClassifier { get; }

    public PendingActionExecutor PendingActionExecutor { get; }

    public PendingActionCallbackService PendingActionCallbackService { get; }

    public AgentTaskService AgentTaskService { get; }

    public TaskCallbackService TaskCallbackService { get; }

    public DocumentIndexingService DocumentIndexingService { get; }

    public DocumentEmbeddingService DocumentEmbeddingService { get; }

    public DocumentRetrievalService DocumentRetrievalService { get; }

    public DocumentQuestionAnsweringService DocumentQuestionAnsweringService { get; }

    public DocumentSummaryService DocumentSummaryService { get; }

    public IImageDescriptionService ImageDescriptionService { get; }

    public AgentRunner AgentRunner { get; }

    public ConversationService ConversationService { get; }

    public CommandRouter CommandRouter { get; }

    public TelegramUpdateHandler TelegramUpdateHandler { get; }

    public ConsoleInputHandler ConsoleInputHandler { get; }

    public TaskReminderLoop TaskReminderLoop { get; }

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables.Reverse())
        {
            disposable.Dispose();
        }
    }
}
