using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class DocumentIndexingService
{
    private readonly DocumentStorageService _documentStorage;

    public DocumentIndexingService(DocumentStorageService documentStorage)
    {
        _documentStorage = documentStorage;
    }

    public async Task<int> IndexFileAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int fileId,
        CancellationToken cancellationToken)
    {
        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == fileId && x.ConnectedUserId == user.Id, cancellationToken);

        if (file is null)
        {
            throw new InvalidOperationException($"File #{fileId} was not found.");
        }

        string text = await _documentStorage.ExtractTextAsync(file, cancellationToken, maxCharacters: 200_000);
        IReadOnlyList<string> chunks = DocumentChunker.Split(text);

        List<DocumentChunk> oldChunks = await dbContext.DocumentChunks
            .Where(x => x.UploadedFileId == file.Id && x.ConnectedUserId == user.Id)
            .ToListAsync(cancellationToken);
        dbContext.DocumentChunks.RemoveRange(oldChunks);

        int chunkNumber = 1;
        foreach (string chunk in chunks)
        {
            dbContext.DocumentChunks.Add(new DocumentChunk
            {
                UploadedFileId = file.Id,
                ConnectedUserId = user.Id,
                ChatId = user.ChatId,
                ChunkNumber = chunkNumber++,
                OriginalFileName = file.OriginalFileName,
                Text = chunk,
                CharacterCount = chunk.Length,
                CreatedAt = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return chunks.Count;
    }

    public async Task<(int FilesIndexed, int ChunksCreated, int Skipped)> IndexAllAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        CancellationToken cancellationToken)
    {
        List<int> fileIds = await dbContext.UploadedFiles
            .Where(x => x.ConnectedUserId == user.Id)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        int filesIndexed = 0;
        int chunksCreated = 0;
        int skipped = 0;
        foreach (int fileId in fileIds)
        {
            int count = await IndexFileAsync(dbContext, user, fileId, cancellationToken);
            if (count == 0)
            {
                skipped++;
            }
            else
            {
                filesIndexed++;
                chunksCreated += count;
            }
        }

        return (filesIndexed, chunksCreated, skipped);
    }
}
