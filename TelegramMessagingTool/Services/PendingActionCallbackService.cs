using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Telegram;

namespace TelegramMessagingTool.Services;

public sealed class PendingActionCallbackService
{
    private readonly PendingActionService _pendingActionService;
    private readonly PendingActionExecutor _pendingActionExecutor;
    private readonly BotSettings _settings;
    private readonly RuntimeObservabilityService _observability;

    public PendingActionCallbackService(
        PendingActionService pendingActionService,
        PendingActionExecutor pendingActionExecutor,
        BotSettings settings,
        RuntimeObservabilityService? observability = null)
    {
        _pendingActionService = pendingActionService;
        _pendingActionExecutor = pendingActionExecutor;
        _settings = settings;
        _observability = observability ?? new RuntimeObservabilityService();
    }

    public async Task<PendingActionCallbackResult> HandleAsync(
        string? callbackData,
        ConnectedUser user,
        long actorTelegramUserId,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!PendingActionCallbackParser.TryParse(callbackData, out PendingActionCallback callback))
        {
            return PendingActionCallbackResult.NotHandled;
        }

        if (!BotAccessPolicy.IsAdmin(actorTelegramUserId, _settings.AdminChatId))
        {
            _observability.CallbackDecisionRejected("pending_action", callback.Verb.ToString().ToLowerInvariant(), callback.ActionId, actorTelegramUserId, user.ChatId, "unauthorized_actor");
            return UnauthorizedActorResult;
        }

        _observability.CallbackDecisionReceived("pending_action", callback.Verb.ToString().ToLowerInvariant(), callback.ActionId, actorTelegramUserId, user.ChatId);

        return callback.Verb switch
        {
            PendingActionCallbackVerb.Approve => await ApproveAsync(callback.ActionId, user, dbContext, cancellationToken),
            PendingActionCallbackVerb.Deny => await DenyAsync(callback.ActionId, user, dbContext, cancellationToken),
            PendingActionCallbackVerb.Details => await DetailsAsync(callback.ActionId, user, dbContext, cancellationToken),
            _ => PendingActionCallbackResult.NotHandled
        };
    }

    private static PendingActionCallbackResult UnauthorizedActorResult => new(
        Handled: true,
        AnswerText: "Not authorized",
        MessageText: "This button is not authorized for your Telegram account.");

    private async Task<PendingActionCallbackResult> ApproveAsync(
        int actionId,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        PendingActionDecision decision = await _pendingActionService.ApproveAsync(dbContext, user, actionId, cancellationToken);
        if (!decision.Success || decision.Action is null)
        {
            return new PendingActionCallbackResult(true, "Approve failed", decision.Message);
        }

        PendingActionExecutionResult executionResult = await _pendingActionExecutor.ExecuteApprovedAsync(dbContext, decision.Action, cancellationToken);
        string executionHeading = executionResult.Executed ? "Execution result" : "No automatic execution";
        string messageText = $"""
Approved action #{decision.Action.Id}: {decision.Action.ToolName}

{executionHeading}:
{executionResult.Message}
""";

        return new PendingActionCallbackResult(true, "Approved", messageText);
    }

    private async Task<PendingActionCallbackResult> DenyAsync(
        int actionId,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        PendingActionDecision decision = await _pendingActionService.DenyAsync(dbContext, user, actionId, cancellationToken);
        return new PendingActionCallbackResult(true, decision.Success ? "Denied" : "Deny failed", decision.Message);
    }

    private async Task<PendingActionCallbackResult> DetailsAsync(
        int actionId,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        PendingAction? action = await _pendingActionService.GetForUserAsync(dbContext, user, actionId, cancellationToken);
        if (action is null)
        {
            return new PendingActionCallbackResult(true, "Not found", $"No action #{actionId} was found for your account.");
        }

        return new PendingActionCallbackResult(true, "Details", RenderActionDetails(action));
    }

    private static string RenderActionDetails(PendingAction action)
    {
        string decisionTime = action.DecidedAt is null
            ? "not decided"
            : action.DecidedAt.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        string decisionNote = string.IsNullOrWhiteSpace(action.DecisionNote)
            ? "none"
            : action.DecisionNote;

        return $"""
Action #{action.Id}

Type: {action.ToolName}
Risk: {action.RiskLevel}
Status: {action.Status}
Description: {action.Description}

Created UTC: {action.CreatedAt:yyyy-MM-dd HH:mm:ss}
Expires UTC: {action.ExpiresAt:yyyy-MM-dd HH:mm:ss}
Decided UTC: {decisionTime}

Decision note:
{decisionNote}

Payload:
{Truncate(action.PayloadJson, 1000)}
""";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "{}";
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}

public sealed record PendingActionCallbackResult(bool Handled, string AnswerText, string? MessageText)
{
    public static PendingActionCallbackResult NotHandled { get; } = new(false, "Unsupported action", null);
}
