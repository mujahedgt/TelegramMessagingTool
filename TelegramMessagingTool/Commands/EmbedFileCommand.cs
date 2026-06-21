using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class EmbedFileCommand : IBotCommand
{
    private readonly DocumentIndexingService _indexingService;
    private readonly DocumentEmbeddingService _embeddingService;

    public EmbedFileCommand(DocumentIndexingService indexingService, DocumentEmbeddingService embeddingService)
    {
        _indexingService = indexingService;
        _embeddingService = embeddingService;
    }

    public string Name => "/embedfile";

    public string Description => "Generate local embeddings for one indexed document.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/embedfile", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        string args = messageText["/embedfile".Length..].Trim();
        if (!int.TryParse(args, out int fileId))
        {
            return new CommandResult(true, "Usage: /embedfile <file id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == fileId && x.ConnectedUserId == user.Id, cancellationToken);
        if (file is null)
        {
            return new CommandResult(true, $"File #{fileId} was not found.");
        }

        bool hasChunks = await dbContext.DocumentChunks
            .AnyAsync(x => x.UploadedFileId == fileId && x.ConnectedUserId == user.Id, cancellationToken);
        if (!hasChunks)
        {
            int indexedChunks = await _indexingService.IndexFileAsync(dbContext, user, fileId, cancellationToken);
            if (indexedChunks == 0)
            {
                return new CommandResult(true, $"File #{fileId} has no extractable text to embed. If it is a scanned PDF, OCR is not supported yet.");
            }
        }

        try
        {
            int embedded = await _embeddingService.EmbedFileAsync(dbContext, user, fileId, cancellationToken);
            return new CommandResult(true, $"Embedded chunks for file #{fileId}: {embedded}. Semantic document search can use these embeddings when ENABLE_DOCUMENT_EMBEDDINGS=true.");
        }
        catch (InvalidOperationException ex)
        {
            return new CommandResult(true, ex.Message);
        }
    }
}
