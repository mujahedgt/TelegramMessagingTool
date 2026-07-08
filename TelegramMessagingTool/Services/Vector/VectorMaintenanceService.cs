using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services.Vector;

public sealed class VectorMaintenanceService
{
    private readonly DocumentIndexingService _indexingService;
    private readonly DocumentEmbeddingService _embeddingService;
    private readonly IVectorStore? _vectorStore;

    public VectorMaintenanceService(
        DocumentIndexingService indexingService,
        DocumentEmbeddingService embeddingService,
        IVectorStore? vectorStore)
    {
        _indexingService = indexingService;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
    }

    public async Task<VectorMaintenanceResult> SyncAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        CancellationToken cancellationToken)
    {
        if (!await HasSavedFilesAsync(dbContext, user, cancellationToken))
        {
            return new VectorMaintenanceResult(0, 0, 0, "No saved files found yet. Upload or create files first.");
        }

        await EnsureIndexedAsync(dbContext, user, cancellationToken);
        (int filesEmbedded, int chunksEmbedded) = await _embeddingService.EmbedAllAsync(dbContext, user, cancellationToken);
        return new VectorMaintenanceResult(filesEmbedded, chunksEmbedded, 0, $"Vector sync complete. Files updated: {filesEmbedded}. Chunks synced: {chunksEmbedded}.");
    }

    public async Task<VectorMaintenanceResult> RepairAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        CancellationToken cancellationToken)
    {
        if (!await HasSavedFilesAsync(dbContext, user, cancellationToken))
        {
            return new VectorMaintenanceResult(0, 0, 0, "No saved files found yet. Upload or create files first.");
        }

        await EnsureIndexedAsync(dbContext, user, cancellationToken);
        (int filesEmbedded, int chunksEmbedded) = await _embeddingService.EmbedAllAsync(dbContext, user, cancellationToken);
        return new VectorMaintenanceResult(filesEmbedded, chunksEmbedded, 0, $"Vector repair complete. Files updated: {filesEmbedded}. Chunks repaired: {chunksEmbedded}.");
    }

    public async Task<VectorMaintenanceResult> ClearAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        CancellationToken cancellationToken)
    {
        if (_vectorStore is null)
        {
            return new VectorMaintenanceResult(0, 0, 0, "No external vector store is enabled. Current provider uses SQL embeddings only.");
        }

        List<int> uploadedFileIds = await dbContext.UploadedFiles
            .Where(x => x.ConnectedUserId == user.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (int uploadedFileId in uploadedFileIds)
        {
            await _vectorStore.DeleteByUploadedFileIdAsync(uploadedFileId, cancellationToken);
        }

        return new VectorMaintenanceResult(0, 0, uploadedFileIds.Count, $"Vector clear complete. Files cleared from vector store: {uploadedFileIds.Count}.");
    }

    private static Task<bool> HasSavedFilesAsync(TelegramDbContext dbContext, ConnectedUser user, CancellationToken cancellationToken)
    {
        return dbContext.UploadedFiles.AnyAsync(x => x.ConnectedUserId == user.Id, cancellationToken);
    }

    private async Task EnsureIndexedAsync(TelegramDbContext dbContext, ConnectedUser user, CancellationToken cancellationToken)
    {
        bool hasChunks = await dbContext.DocumentChunks.AnyAsync(x => x.ConnectedUserId == user.Id, cancellationToken);
        if (!hasChunks)
        {
            await _indexingService.IndexAllAsync(dbContext, user, cancellationToken);
        }
    }
}

public sealed record VectorMaintenanceResult(int FilesUpdated, int ChunksUpdated, int FilesCleared, string Message);
