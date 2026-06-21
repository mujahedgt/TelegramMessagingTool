using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class DocumentEmbeddingService
{
    private readonly ITextEmbeddingService _embeddingService;
    private readonly string _embeddingModel;

    public DocumentEmbeddingService(ITextEmbeddingService embeddingService, string embeddingModel)
    {
        _embeddingService = embeddingService;
        _embeddingModel = string.IsNullOrWhiteSpace(embeddingModel)
            ? BotConfiguration.DefaultEmbeddingModel
            : embeddingModel.Trim();
    }

    public async Task<int> EmbedFileAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int fileId,
        CancellationToken cancellationToken)
    {
        List<DocumentChunk> chunks = await dbContext.DocumentChunks
            .Where(x => x.ConnectedUserId == user.Id && x.UploadedFileId == fileId)
            .OrderBy(x => x.ChunkNumber)
            .ToListAsync(cancellationToken);

        int updated = 0;
        foreach (DocumentChunk chunk in chunks)
        {
            IReadOnlyList<float> embedding = await _embeddingService.EmbedAsync(chunk.Text, cancellationToken);
            if (embedding.Count == 0)
            {
                continue;
            }

            chunk.EmbeddingJson = EmbeddingMath.Serialize(embedding);
            chunk.EmbeddingModel = _embeddingModel;
            chunk.EmbeddingUpdatedAt = DateTime.UtcNow;
            updated++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public async Task<(int FilesEmbedded, int ChunksEmbedded)> EmbedAllAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        CancellationToken cancellationToken)
    {
        List<int> fileIds = await dbContext.DocumentChunks
            .Where(x => x.ConnectedUserId == user.Id)
            .Select(x => x.UploadedFileId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        int filesEmbedded = 0;
        int chunksEmbedded = 0;
        foreach (int fileId in fileIds)
        {
            int count = await EmbedFileAsync(dbContext, user, fileId, cancellationToken);
            if (count > 0)
            {
                filesEmbedded++;
                chunksEmbedded += count;
            }
        }

        return (filesEmbedded, chunksEmbedded);
    }
}
