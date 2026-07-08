namespace TelegramMessagingTool.Services.Vector;

public interface IVectorStore
{
    Task UpsertAsync(DocumentVector vector, CancellationToken cancellationToken);

    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        long chatId,
        IReadOnlyList<float> queryEmbedding,
        int limit,
        CancellationToken cancellationToken);

    Task DeleteByUploadedFileIdAsync(int uploadedFileId, CancellationToken cancellationToken);
}
