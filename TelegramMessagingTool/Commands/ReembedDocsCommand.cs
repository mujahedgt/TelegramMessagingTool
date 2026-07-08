using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ReembedDocsCommand : IBotCommand
{
    private readonly DocumentIndexingService _indexingService;
    private readonly DocumentEmbeddingService _embeddingService;

    public ReembedDocsCommand(DocumentIndexingService indexingService, DocumentEmbeddingService embeddingService)
    {
        _indexingService = indexingService;
        _embeddingService = embeddingService;
    }

    public string Name => "/reembeddocs";

    public string Description => "Rebuild embeddings for all indexed documents and configured vector-store fallback.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        bool hasFiles = await dbContext.UploadedFiles
            .AnyAsync(x => x.ConnectedUserId == user.Id, cancellationToken);
        if (!hasFiles)
        {
            return new CommandResult(true, "No saved files found yet. Upload or create files first.");
        }

        bool hasChunks = await dbContext.DocumentChunks
            .AnyAsync(x => x.ConnectedUserId == user.Id, cancellationToken);
        if (!hasChunks)
        {
            await _indexingService.IndexAllAsync(dbContext, user, cancellationToken);
        }

        try
        {
            (int filesEmbedded, int chunksEmbedded) = await _embeddingService.EmbedAllAsync(dbContext, user, cancellationToken);
            return new CommandResult(true, $"Rebuilt embeddings across documents: {chunksEmbedded}. Files updated: {filesEmbedded}. If VECTOR_STORE_PROVIDER=local_json, vectors were mirrored into the local vector store.");
        }
        catch (InvalidOperationException ex)
        {
            return new CommandResult(true, ex.Message);
        }
    }
}
