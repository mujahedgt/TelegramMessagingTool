using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class CancelCommand : IBotCommand
{
    private readonly AgentTaskService _agentTaskService;

    public CancelCommand(AgentTaskService agentTaskService)
    {
        _agentTaskService = agentTaskService;
    }

    public string Name => "/cancel";
    public string Description => "Cancel an active task plan.";

    public async Task<CommandResult> TryHandleAsync(Message message, ConnectedUser user, TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        string rawId = CommandParser.GetArguments(messageText, Name);
        if (!int.TryParse(rawId, out int taskId) || taskId <= 0)
        {
            return new CommandResult(true, "Usage: /cancel <task-id>");
        }

        TaskUpdateResult result = await _agentTaskService.CancelAsync(dbContext, user, taskId, cancellationToken);
        string details = result.Task is null ? string.Empty : "\n\n" + AgentTaskService.RenderTask(result.Task);
        return new CommandResult(true, result.Message + details);
    }
}
