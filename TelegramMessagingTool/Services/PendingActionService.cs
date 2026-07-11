using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class PendingActionService
{
    private readonly RuntimeObservabilityService _observability;

    public PendingActionService(RuntimeObservabilityService? observability = null)
    {
        _observability = observability ?? new RuntimeObservabilityService();
    }

    public async Task<PendingAction> CreateAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        string toolName,
        string description,
        string payloadJson,
        string riskLevel,
        TimeSpan? ttl,
        CancellationToken cancellationToken)
    {
        var pendingAction = new PendingAction
        {
            ConnectedUserId = user.Id,
            ChatId = user.ChatId,
            ToolName = TrimTo(toolName, 100),
            Description = TrimTo(description, 1000),
            PayloadJson = TrimTo(payloadJson, 4000),
            RiskLevel = string.IsNullOrWhiteSpace(riskLevel) ? "medium" : TrimTo(riskLevel, 50),
            Status = PendingActionStatuses.Pending,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(30))
        };

        dbContext.PendingActions.Add(pendingAction);
        await dbContext.SaveChangesAsync(cancellationToken);
        _observability.PendingActionCreated(pendingAction.Id, pendingAction.ToolName, pendingAction.RiskLevel);
        return pendingAction;
    }

    public async Task<IReadOnlyList<PendingAction>> ListPendingAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        CancellationToken cancellationToken)
    {
        await MarkExpiredAsync(dbContext, user, cancellationToken);

        return await dbContext.PendingActions
            .Where(x => x.ConnectedUserId == user.Id && x.Status == PendingActionStatuses.Pending)
            .OrderBy(x => x.ExpiresAt)
            .Take(20)
            .ToListAsync(cancellationToken);
    }

    public async Task<PendingAction?> GetForUserAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int actionId,
        CancellationToken cancellationToken)
    {
        await MarkExpiredAsync(dbContext, user, cancellationToken);
        return await dbContext.PendingActions
            .FirstOrDefaultAsync(x => x.Id == actionId && x.ConnectedUserId == user.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingAction>> ListRecentAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int count,
        CancellationToken cancellationToken)
    {
        await MarkExpiredAsync(dbContext, user, cancellationToken);
        int boundedCount = Math.Clamp(count, 1, 50);
        return await dbContext.PendingActions
            .Where(x => x.ConnectedUserId == user.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(boundedCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<PendingActionDecision> ApproveAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int actionId,
        CancellationToken cancellationToken)
    {
        return await DecideAsync(dbContext, user, actionId, PendingActionStatuses.Approved, "Approved by user.", cancellationToken);
    }

    public async Task<PendingActionDecision> DenyAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int actionId,
        CancellationToken cancellationToken)
    {
        return await DecideAsync(dbContext, user, actionId, PendingActionStatuses.Denied, "Denied by user.", cancellationToken);
    }

    private async Task<PendingActionDecision> DecideAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int actionId,
        string newStatus,
        string note,
        CancellationToken cancellationToken)
    {
        PendingAction? action = await dbContext.PendingActions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == actionId && x.ConnectedUserId == user.Id, cancellationToken);

        if (action is null)
        {
            return new PendingActionDecision(false, null, $"No pending action #{actionId} was found for your account.");
        }

        DateTime now = DateTime.UtcNow;
        if (action.Status != PendingActionStatuses.Pending)
        {
            return new PendingActionDecision(false, action, $"Action #{action.Id} is already {action.Status}.");
        }

        if (action.ExpiresAt <= now)
        {
            int expiredRows = await dbContext.PendingActions
                .Where(x => x.Id == actionId
                    && x.ConnectedUserId == user.Id
                    && x.Status == PendingActionStatuses.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, PendingActionStatuses.Expired)
                    .SetProperty(x => x.DecidedAt, now)
                    .SetProperty(x => x.DecisionNote, "Expired before user decision."), cancellationToken);

            PendingAction expiredAction = await ReloadActionAsync(dbContext, actionId, user.Id, cancellationToken) ?? action;
            string message = expiredRows == 0
                ? $"Action #{action.Id} is already {expiredAction.Status}."
                : $"Action #{action.Id} expired before it was approved or denied.";
            return new PendingActionDecision(false, expiredAction, message);
        }

        int changedRows = await dbContext.PendingActions
            .Where(x => x.Id == actionId
                && x.ConnectedUserId == user.Id
                && x.Status == PendingActionStatuses.Pending
                && x.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, newStatus)
                .SetProperty(x => x.DecidedAt, now)
                .SetProperty(x => x.DecisionNote, note), cancellationToken);

        PendingAction decidedAction = await ReloadActionAsync(dbContext, actionId, user.Id, cancellationToken) ?? action;
        if (changedRows == 0)
        {
            return new PendingActionDecision(false, decidedAction, $"Action #{action.Id} is already {decidedAction.Status}.");
        }

        _observability.PendingActionDecision(decidedAction.Id, decidedAction.ToolName, decidedAction.Status);
        return new PendingActionDecision(true, decidedAction, $"Action #{decidedAction.Id} was {newStatus}.");
    }

    private static async Task<PendingAction?> ReloadActionAsync(
        TelegramDbContext dbContext,
        int actionId,
        int connectedUserId,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        return await dbContext.PendingActions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == actionId && x.ConnectedUserId == connectedUserId, cancellationToken);
    }

    private static async Task MarkExpiredAsync(TelegramDbContext dbContext, ConnectedUser user, CancellationToken cancellationToken)
    {
        List<PendingAction> expired = await dbContext.PendingActions
            .Where(x => x.ConnectedUserId == user.Id
                && x.Status == PendingActionStatuses.Pending
                && x.ExpiresAt <= DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return;
        }

        foreach (PendingAction action in expired)
        {
            action.Status = PendingActionStatuses.Expired;
            action.DecidedAt = DateTime.UtcNow;
            action.DecisionNote = "Expired before user decision.";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string TrimTo(string value, int maxLength)
    {
        value = value.Trim();
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

public sealed record PendingActionDecision(bool Success, PendingAction? Action, string Message);
