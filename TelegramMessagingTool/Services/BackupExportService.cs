using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class BackupExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly DocumentStorageService _documentStorage;

    public BackupExportService(DocumentStorageService documentStorage)
    {
        _documentStorage = documentStorage;
    }

    public async Task<BackupExportResult> ExportUserDataAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        CancellationToken cancellationToken)
    {
        List<ChatMessage> messages = await dbContext.Messages
            .AsNoTracking()
            .Where(x => x.ConnectedUserId == user.Id && x.ChatId == user.ChatId)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(cancellationToken);

        List<Memory> memories = await dbContext.Memories
            .AsNoTracking()
            .Where(x => x.ConnectedUserId == user.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        List<UploadedFile> files = await dbContext.UploadedFiles
            .AsNoTracking()
            .Where(x => x.ConnectedUserId == user.Id && x.ChatId == user.ChatId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        List<DocumentChunk> chunks = await dbContext.DocumentChunks
            .AsNoTracking()
            .Where(x => x.ConnectedUserId == user.Id && x.ChatId == user.ChatId)
            .OrderBy(x => x.UploadedFileId)
            .ThenBy(x => x.ChunkNumber)
            .ToListAsync(cancellationToken);

        List<AgentTask> tasks = await dbContext.AgentTasks
            .AsNoTracking()
            .Include(x => x.Steps)
            .Where(x => x.ConnectedUserId == user.Id && x.ChatId == user.ChatId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        List<PendingAction> actions = await dbContext.PendingActions
            .AsNoTracking()
            .Where(x => x.ConnectedUserId == user.Id && x.ChatId == user.ChatId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var payload = new
        {
            exportedAtUtc = DateTime.UtcNow,
            scope = new
            {
                chatId = user.ChatId,
                connectedUserId = user.Id
            },
            counts = new
            {
                messages = messages.Count,
                memories = memories.Count,
                files = files.Count,
                documentChunks = chunks.Count,
                tasks = tasks.Count,
                pendingActions = actions.Count
            },
            messages = messages.Select(x => new
            {
                x.Id,
                role = x.Role.ToString(),
                x.Content,
                timestampUtc = x.Timestamp.ToUniversalTime()
            }),
            memories = memories.Select(x => new
            {
                x.Id,
                x.Content,
                createdAtUtc = x.CreatedAt.ToUniversalTime(),
                updatedAtUtc = x.UpdatedAt.ToUniversalTime()
            }),
            files = files.Select(x => new
            {
                x.Id,
                x.OriginalFileName,
                x.RelativePath,
                x.ContentType,
                x.SizeBytes,
                x.Source,
                createdAtUtc = x.CreatedAt.ToUniversalTime()
            }),
            documentChunks = chunks.Select(x => new
            {
                x.Id,
                x.UploadedFileId,
                x.OriginalFileName,
                x.ChunkNumber,
                x.CharacterCount,
                hasEmbedding = !string.IsNullOrWhiteSpace(x.EmbeddingJson),
                x.EmbeddingModel,
                createdAtUtc = x.CreatedAt.ToUniversalTime()
            }),
            tasks = tasks.Select(x => new
            {
                x.Id,
                x.Goal,
                x.Status,
                createdAtUtc = x.CreatedAt.ToUniversalTime(),
                updatedAtUtc = x.UpdatedAt.ToUniversalTime(),
                completedAtUtc = x.CompletedAt?.ToUniversalTime(),
                steps = x.Steps
                    .OrderBy(step => step.StepNumber)
                    .Select(step => new
                    {
                        step.Id,
                        step.StepNumber,
                        step.Description,
                        step.IsDone,
                        createdAtUtc = step.CreatedAt.ToUniversalTime(),
                        completedAtUtc = step.CompletedAt?.ToUniversalTime(),
                        scheduledAtUtc = step.ScheduledAtUtc?.ToUniversalTime(),
                        reminderSentAtUtc = step.ReminderSentAtUtc?.ToUniversalTime(),
                        step.ScheduleNote
                    })
            }),
            pendingActions = actions.Select(x => new
            {
                x.Id,
                x.ToolName,
                x.Description,
                x.RiskLevel,
                x.Status,
                createdAtUtc = x.CreatedAt.ToUniversalTime(),
                expiresAtUtc = x.ExpiresAt.ToUniversalTime(),
                decidedAtUtc = x.DecidedAt?.ToUniversalTime(),
                x.DecisionNote
            })
        };

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        string fileName = $"telegram-data-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json";
        UploadedFile exportFile = await _documentStorage.CreateTextFileAsync(user, fileName, json, cancellationToken);
        dbContext.UploadedFiles.Add(exportFile);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BackupExportResult(exportFile, messages.Count, memories.Count, files.Count, chunks.Count, tasks.Count, actions.Count);
    }
}

public sealed record BackupExportResult(
    UploadedFile File,
    int MessageCount,
    int MemoryCount,
    int FileCount,
    int DocumentChunkCount,
    int TaskCount,
    int PendingActionCount);
