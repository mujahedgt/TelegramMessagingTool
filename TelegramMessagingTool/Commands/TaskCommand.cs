using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class TaskCommand : IBotCommand
{
    private readonly AgentTaskService _agentTaskService;

    public TaskCommand(AgentTaskService agentTaskService)
    {
        _agentTaskService = agentTaskService;
    }

    public string Name => "/task";
    public string Description => "Show one task plan by ID.";

    public async Task<CommandResult> TryHandleAsync(Message message, ConnectedUser user, TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/task", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        if (!TryParseTaskId(messageText, "/task", out int taskId))
        {
            return new CommandResult(true, "Usage: /task <task-id>");
        }

        AgentTask? task = await _agentTaskService.GetAsync(dbContext, user, taskId, cancellationToken);
        return task is null
            ? new CommandResult(true, $"Task #{taskId} was not found.")
            : new CommandResult(true, AgentTaskService.RenderTask(task));
    }

    private static bool TryParseTaskId(string messageText, string commandName, out int taskId)
    {
        string rawId = messageText[commandName.Length..].Trim();
        return int.TryParse(rawId, out taskId) && taskId > 0;
    }
}
