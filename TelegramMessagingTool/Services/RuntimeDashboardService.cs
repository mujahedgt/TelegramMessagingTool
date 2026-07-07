using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class RuntimeDashboardService
{
    private readonly BotSettings _settings;
    private readonly RuntimeEventBuffer _runtimeEventBuffer;
    private readonly DateTimeOffset _startedAt;

    public RuntimeDashboardService(BotSettings settings, RuntimeEventBuffer runtimeEventBuffer, DateTimeOffset? startedAt = null)
    {
        _settings = settings;
        _runtimeEventBuffer = runtimeEventBuffer;
        _startedAt = startedAt ?? DateTimeOffset.UtcNow;
    }

    public async Task<string> RenderAsync(TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        int activeTasks = await dbContext.AgentTasks
            .AsNoTracking()
            .CountAsync(x => x.Status == AgentTaskStatuses.Active, cancellationToken);

        int pendingApprovals = await dbContext.PendingActions
            .AsNoTracking()
            .CountAsync(x => x.Status == PendingActionStatuses.Pending && x.ExpiresAt > DateTime.UtcNow, cancellationToken);

        int indexedDocs = await dbContext.DocumentChunks
            .AsNoTracking()
            .Select(x => x.UploadedFileId)
            .Distinct()
            .CountAsync(cancellationToken);

        List<string> savedFileNames = await dbContext.UploadedFiles
            .AsNoTracking()
            .Select(x => x.OriginalFileName)
            .ToListAsync(cancellationToken);

        int savedImages = savedFileNames.Count(DocumentStorageService.IsImageFileName);
        int recentWarnings = _runtimeEventBuffer.RecentWarningsAndErrors(50).Count;

        var snapshot = new RuntimeDashboardSnapshot(
            ActiveTasks: activeTasks,
            PendingApprovals: pendingApprovals,
            IndexedDocs: indexedDocs,
            SavedFiles: savedFileNames.Count,
            SavedImages: savedImages,
            RecentWarnings: recentWarnings,
            Uptime: DateTimeOffset.UtcNow - _startedAt,
            AccessMode: BotAccessPolicy.DescribeAccessMode(_settings.AllowedChatIds, _settings.AdminChatId, _settings.AllowPublicAccess),
            DatabaseConnection: _settings.DatabaseConnectionString);

        return AgentConsoleRenderer.RenderDashboard(snapshot);
    }
}
