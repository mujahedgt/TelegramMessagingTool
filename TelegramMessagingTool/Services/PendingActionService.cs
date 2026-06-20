using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class PendingActionService
{
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
            .FirstOrDefaultAsync(x => x.Id == actionId && x.ConnectedUserId == user.Id, cancellationToken);

        if (action is null)
        {
            return new PendingActionDecision(false, null, $"No pending action #{actionId} was found for your account.");
        }

        if (action.Status != PendingActionStatuses.Pending)
        {
            return new PendingActionDecision(false, action, $"Action #{action.Id} is already {action.Status}.");
        }

        if (action.ExpiresAt <= DateTime.UtcNow)
        {
            action.Status = PendingActionStatuses.Expired;
            action.DecidedAt = DateTime.UtcNow;
            action.DecisionNote = "Expired before user decision.";
            await dbContext.SaveChangesAsync(cancellationToken);
            return new PendingActionDecision(false, action, $"Action #{action.Id} expired before it was approved or denied.");
        }

        action.Status = newStatus;
        action.DecidedAt = DateTime.UtcNow;
        action.DecisionNote = note;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PendingActionDecision(true, action, $"Action #{action.Id} was {newStatus}.");
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
