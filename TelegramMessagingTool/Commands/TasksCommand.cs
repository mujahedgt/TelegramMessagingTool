using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class TasksCommand : IBotCommand
{
    private readonly AgentTaskService _agentTaskService;

    public TasksCommand(AgentTaskService agentTaskService)
    {
        _agentTaskService = agentTaskService;
    }

    public string Name => "/tasks";
    public string Description => "List your active task plans.";

    public async Task<CommandResult> TryHandleAsync(Message message, ConnectedUser user, TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        bool includeDone = CommandParser.GetArguments(messageText, Name).Contains("all", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<AgentTask> tasks = await _agentTaskService.ListAsync(dbContext, user, includeDone, cancellationToken);
        if (tasks.Count == 0)
        {
            return new CommandResult(true, includeDone ? "No task plans found." : "No active task plans. Use /plan <goal> to create one.");
        }

        string reply = string.Join("\n\n", tasks.Select(task =>
        {
            int done = task.Steps.Count(x => x.IsDone);
            int total = task.Steps.Count;
            return $"#{task.Id} [{task.Status}] {done}/{total} - {task.Goal}";
        }));

        return new CommandResult(true, "Task plans:\n" + reply + "\n\nUse /task <id> to view details.");
    }
}
