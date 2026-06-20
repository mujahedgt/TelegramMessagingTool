using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class IndexFileCommand : IBotCommand
{
    private readonly DocumentIndexingService _indexingService;

    public IndexFileCommand(DocumentIndexingService indexingService)
    {
        _indexingService = indexingService;
    }

    public string Name => "/indexfile";

    public string Description => "Index a saved file for document Q&A.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/indexfile", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        string idText = messageText["/indexfile".Length..].Trim();
        if (!int.TryParse(idText, out int fileId))
        {
            return new CommandResult(true, "Usage: /indexfile <file id>");
        }

        try
        {
            int chunksCreated = await _indexingService.IndexFileAsync(dbContext, user, fileId, cancellationToken);
            return new CommandResult(true, $"Indexed file #{fileId}.\nChunks created: {chunksCreated}");
        }
        catch (InvalidOperationException ex)
        {
            return new CommandResult(true, ex.Message);
        }
    }
}
