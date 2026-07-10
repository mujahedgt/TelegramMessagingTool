using static TestAssert;
using TelegramMessagingTool;
using TelegramMessagingTool.Commands;
using TelegramMessagingTool.Runtime;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Services.Vector;
using TelegramMessagingTool.Tools;

public static class CommandTests
{
    public static void RunCommandRouterFactoryTests(BotSettings settings)
    {
        using HttpClient httpClient = new();
        var ollamaClient = new OllamaChatClient(httpClient, settings);
        var embeddingClient = new OllamaEmbeddingClient(httpClient, settings);
        var documentStorage = new DocumentStorageService(Path.Combine(Path.GetTempPath(), $"TelegramMessagingTool_CommandFactory_{Guid.NewGuid():N}"));
        string importDirectory = Path.Combine(Path.GetTempPath(), $"TelegramMessagingTool_Import_{Guid.NewGuid():N}");
        var pendingActionService = new PendingActionService();
        var pendingActionExecutor = new PendingActionExecutor(new SystemProcessTerminator(), documentStorage);
        var agentTaskService = new AgentTaskService();
        var documentIndexingService = new DocumentIndexingService(documentStorage);
        var documentRetrievalService = new DocumentRetrievalService();
        var documentQuestionAnsweringService = new DocumentQuestionAnsweringService(ollamaClient);
        var documentSummaryService = new DocumentSummaryService(ollamaClient);
        var transcriptInsightsService = new TranscriptInsightsService(ollamaClient);
        var documentEmbeddingService = new DocumentEmbeddingService(embeddingClient, settings.OllamaEmbeddingModel);
        var vectorMaintenanceService = new VectorMaintenanceService(documentIndexingService, documentEmbeddingService, vectorStore: null);
        var toolRegistry = new ToolRegistry(Array.Empty<IAgentTool>());
        var imageDescriptionService = new OllamaImageDescriptionService(httpClient, settings);
        CommandRouter router = CommandRouterFactory.Create(
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
            vectorMaintenanceService,
            imageDescriptionService,
            textToSpeechService: null);
        string commandNames = string.Join(",", router.Commands.Select(x => x.Name));
        AssertEqual(
            "/help,/systeminfo,/diskstatus,/processes,/status,/health,/selfupdate,/errors,/riskconfig,/reset,/remember,/memory,/forget,/files,/images,/describeimage,/voicefiles,/transcribe,/voicebrief,/voiceplan,/transcriptinsights,/transcripttasks,/speaktext,/sendaudio,/exportchat,/exportdata,/readfile,/createfile,/importfiles,/importfile,/deletefile,/indexfile,/indexdocs,/docchunks,/askfile,/askdocs,/summarizefile,/summarizedocs,/embedfile,/embeddocs,/reembeddocs,/vectorstatus,/vectorsync,/vectorclear,/vectorrepair,/tools,/harnesses,/plugins,/killprocess,/action,/actions,/pending,/approve,/deny,/plan,/tasks,/task,/schedule,/schedulelist,/unschedule,/done,/cancel",
            commandNames,
            "CommandRouterFactory preserves Program command registration order");
    }
}
