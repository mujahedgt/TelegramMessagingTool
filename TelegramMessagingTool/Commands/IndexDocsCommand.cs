using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class IndexDocsCommand : IBotCommand
{
    private readonly DocumentIndexingService _indexingService;

    public IndexDocsCommand(DocumentIndexingService indexingService)
    {
        _indexingService = indexingService;
    }

    public string Name => "/indexdocs";

    public string Description => "Index all saved files for document Q&A.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/indexdocs", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        var result = await _indexingService.IndexAllAsync(dbContext, user, cancellationToken);
        return new CommandResult(
            true,
            $"Indexed {result.FilesIndexed} file(s).\nChunks created: {result.ChunksCreated}\nSkipped empty files: {result.Skipped}");
    }
}
