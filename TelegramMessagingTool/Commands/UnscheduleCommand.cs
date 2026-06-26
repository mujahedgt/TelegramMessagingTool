using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class UnscheduleCommand : IBotCommand
{
    private readonly AgentTaskService _agentTaskService;

    public UnscheduleCommand(AgentTaskService agentTaskService)
    {
        _agentTaskService = agentTaskService;
    }

    public string Name => "/unschedule";
    public string Description => "Clear a scheduled task step reminder.";

    public async Task<CommandResult> TryHandleAsync(Message message, ConnectedUser user, TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        string[] parts = CommandParser.GetArguments(messageText, Name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], out int taskId)
            || taskId <= 0
            || !int.TryParse(parts[1], out int stepNumber)
            || stepNumber <= 0)
        {
            return new CommandResult(true, "Usage: /unschedule <task-id> <step-number>");
        }

        TaskUpdateResult result = await _agentTaskService.UnscheduleStepAsync(dbContext, user, taskId, stepNumber, cancellationToken);
        string details = result.Task is null ? string.Empty : "\n\n" + AgentTaskService.RenderTask(result.Task);
        return new CommandResult(true, result.Message + details);
    }
}
