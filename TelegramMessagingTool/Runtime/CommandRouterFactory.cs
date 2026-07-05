using TelegramMessagingTool.Commands;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.Runtime;

public static class CommandRouterFactory
{
    public static CommandRouter Create(
        BotSettings settings,
        ToolRegistry toolRegistry,
        DocumentStorageService documentStorage,
        string importDirectory,
        PendingActionService pendingActionService,
        PendingActionExecutor pendingActionExecutor,
        AgentTaskService agentTaskService,
        DocumentIndexingService documentIndexingService,
        DocumentRetrievalService documentRetrievalService,
        DocumentQuestionAnsweringService documentQuestionAnsweringService,
        DocumentSummaryService documentSummaryService,
        DocumentEmbeddingService documentEmbeddingService,
        IImageDescriptionService imageDescriptionService)
    {
        return new CommandRouter([
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
            new DescribeImageCommand(settings, documentStorage, imageDescriptionService),
            new VoiceFilesCommand(),
            new TranscribeCommand(settings, documentStorage),
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
            new HarnessesCommand(settings),
            new PluginsCommand(settings),
            new KillProcessCommand(pendingActionService, settings),
            new ActionCommand(pendingActionService, settings),
            new ActionsCommand(pendingActionService, settings),
            new PendingCommand(pendingActionService, settings),
            new ApproveCommand(pendingActionService, pendingActionExecutor, settings),
            new DenyCommand(pendingActionService, settings),
            new PlanCommand(agentTaskService),
            new TasksCommand(agentTaskService),
            new TaskCommand(agentTaskService),
            new ScheduleCommand(agentTaskService),
            new ScheduleListCommand(agentTaskService),
            new UnscheduleCommand(agentTaskService),
            new DoneCommand(agentTaskService),
            new CancelCommand(agentTaskService)
        ]);
    }
}
