using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class DoneCommand : IBotCommand
{
    private readonly AgentTaskService _agentTaskService;

    public DoneCommand(AgentTaskService agentTaskService)
    {
        _agentTaskService = agentTaskService;
    }

    public string Name => "/done";
    public string Description => "Mark a task or task step as done.";

    public async Task<CommandResult> TryHandleAsync(Message message, ConnectedUser user, TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/done", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        string[] parts = messageText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out int taskId) || taskId <= 0)
        {
            return new CommandResult(true, "Usage: /done <task-id> [step-number]");
        }

        int? stepNumber = null;
        if (parts.Length >= 3)
        {
            if (!int.TryParse(parts[2], out int parsedStep) || parsedStep <= 0)
            {
                return new CommandResult(true, "Usage: /done <task-id> [step-number]");
            }

            stepNumber = parsedStep;
        }

        TaskUpdateResult result = await _agentTaskService.MarkDoneAsync(dbContext, user, taskId, stepNumber, cancellationToken);
        string details = result.Task is null ? string.Empty : "\n\n" + AgentTaskService.RenderTask(result.Task);
        return new CommandResult(true, result.Message + details);
    }
}
