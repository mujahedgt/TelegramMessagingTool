using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class PlanCommand : IBotCommand
{
    private readonly AgentTaskService _agentTaskService;

    public PlanCommand(AgentTaskService agentTaskService)
    {
        _agentTaskService = agentTaskService;
    }

    public string Name => "/plan";
    public string Description => "Create a task plan for a goal.";

    public async Task<CommandResult> TryHandleAsync(Message message, ConnectedUser user, TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/plan", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        string goal = messageText["/plan".Length..].Trim();
        if (string.IsNullOrWhiteSpace(goal))
        {
            return new CommandResult(true, "Usage: /plan <goal to break into steps>");
        }

        AgentTask task = await _agentTaskService.CreatePlanAsync(dbContext, user, goal, cancellationToken);
        return new CommandResult(true, "Plan created.\n\n" + AgentTaskService.RenderTask(task));
    }
}
