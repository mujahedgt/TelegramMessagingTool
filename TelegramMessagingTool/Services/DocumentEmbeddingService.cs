using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services.Vector;

namespace TelegramMessagingTool.Services;

public sealed class DocumentEmbeddingService
{
    private readonly ITextEmbeddingService _embeddingService;
    private readonly string _embeddingModel;
    private readonly IVectorStore? _vectorStore;

    public DocumentEmbeddingService(ITextEmbeddingService embeddingService, string embeddingModel, IVectorStore? vectorStore = null)
    {
        _embeddingService = embeddingService;
        _embeddingModel = string.IsNullOrWhiteSpace(embeddingModel)
            ? BotConfiguration.DefaultEmbeddingModel
            : embeddingModel.Trim();
        _vectorStore = vectorStore;
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
            if (_vectorStore is not null)
            {
                await _vectorStore.UpsertAsync(ToDocumentVector(chunk, embedding), cancellationToken);
            }

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

    private static DocumentVector ToDocumentVector(DocumentChunk chunk, IReadOnlyList<float> embedding)
    {
        return new DocumentVector(
            Id: $"chunk-{chunk.Id}",
            ChatId: chunk.ChatId,
            ConnectedUserId: chunk.ConnectedUserId,
            UploadedFileId: chunk.UploadedFileId,
            ChunkId: chunk.Id,
            ChunkNumber: chunk.ChunkNumber,
            OriginalFileName: chunk.OriginalFileName,
            Text: chunk.Text,
            Embedding: embedding);
    }
}
