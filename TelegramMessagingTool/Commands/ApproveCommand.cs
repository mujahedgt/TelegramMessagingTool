using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ApproveCommand : IBotCommand
{
    private readonly PendingActionService _pendingActionService;
    private readonly PendingActionExecutor _pendingActionExecutor;
    private readonly BotSettings _settings;

    public ApproveCommand(PendingActionService pendingActionService, PendingActionExecutor pendingActionExecutor, BotSettings settings)
    {
        _pendingActionService = pendingActionService;
        _pendingActionExecutor = pendingActionExecutor;
        _settings = settings;
    }

    public string Name => "/approve";
    public string Description => "Approve a pending action by ID.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return new CommandResult(true, BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId));
        }

        if (!TryParseActionId(messageText, "/approve", out int actionId))
        {
            return new CommandResult(true, "Usage: /approve <pending-action-id>");
        }

        PendingActionDecision decision = await _pendingActionService.ApproveAsync(dbContext, user, actionId, cancellationToken);
        if (!decision.Success || decision.Action is null)
        {
            return new CommandResult(true, decision.Message);
        }

        PendingActionExecutionResult executionResult = await _pendingActionExecutor.ExecuteApprovedAsync(dbContext, decision.Action, cancellationToken);
        string executionHeading = executionResult.Executed ? "Execution result" : "No automatic execution";

        return new CommandResult(true, $"""
Approved action #{decision.Action.Id}: {decision.Action.ToolName}

{executionHeading}:
{executionResult.Message}
""");
    }

    private static bool TryParseActionId(string messageText, string commandName, out int actionId)
    {
        string rawId = CommandParser.GetArguments(messageText, commandName);
        return int.TryParse(rawId, out actionId) && actionId > 0;
    }
}
