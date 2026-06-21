using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class EmbedDocsCommand : IBotCommand
{
    private readonly DocumentIndexingService _indexingService;
    private readonly DocumentEmbeddingService _embeddingService;

    public EmbedDocsCommand(DocumentIndexingService indexingService, DocumentEmbeddingService embeddingService)
    {
        _indexingService = indexingService;
        _embeddingService = embeddingService;
    }

    public string Name => "/embeddocs";

    public string Description => "Generate local embeddings for all indexed documents.";

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
            return new CommandResult(true, $"Embedded chunks across documents: {chunksEmbedded}. Files updated: {filesEmbedded}. Semantic document search can use these embeddings when ENABLE_DOCUMENT_EMBEDDINGS=true.");
        }
        catch (InvalidOperationException ex)
        {
            return new CommandResult(true, ex.Message);
        }
    }
}
