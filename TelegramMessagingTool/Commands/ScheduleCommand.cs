using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ScheduleCommand : IBotCommand
{
    private readonly AgentTaskService _agentTaskService;

    public ScheduleCommand(AgentTaskService agentTaskService)
    {
        _agentTaskService = agentTaskService;
    }

    public string Name => "/schedule";
    public string Description => "Schedule a task step reminder time.";

    public async Task<CommandResult> TryHandleAsync(Message message, ConnectedUser user, TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        string arguments = CommandParser.GetArguments(messageText, Name);
        if (!TryParseArguments(arguments, DateTime.UtcNow, out int taskId, out int stepNumber, out ScheduleParseResult schedule, out string note))
        {
            return new CommandResult(true, Usage());
        }

        TaskUpdateResult result = await _agentTaskService.ScheduleStepAsync(
            dbContext,
            user,
            taskId,
            stepNumber,
            schedule.ScheduledAtUtc,
            note,
            cancellationToken);

        string details = result.Task is null ? string.Empty : "\n\n" + AgentTaskService.RenderTask(result.Task);
        return new CommandResult(true, result.Message + details);
    }

    public static bool TryParseArguments(
        string arguments,
        DateTime nowUtc,
        out int taskId,
        out int stepNumber,
        out ScheduleParseResult schedule,
        out string note)
    {
        taskId = 0;
        stepNumber = 0;
        schedule = ScheduleParseResult.Empty;
        note = string.Empty;

        string[] parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3
            || !int.TryParse(parts[0], out taskId)
            || taskId <= 0
            || !int.TryParse(parts[1], out stepNumber)
            || stepNumber <= 0)
        {
            return false;
        }

        string[] remaining = parts[2..];
        int maxScheduleTokens = Math.Min(3, remaining.Length);
        for (int tokenCount = maxScheduleTokens; tokenCount >= 1; tokenCount--)
        {
            string candidate = string.Join(' ', remaining.Take(tokenCount));
            if (ScheduleParser.TryParse(candidate, nowUtc, out schedule))
            {
                note = string.Join(' ', remaining.Skip(tokenCount)).Trim();
                return true;
            }
        }

        return false;
    }

    private static string Usage()
    {
        return "Usage: /schedule <task-id> <step-number> <time> [note]\n" +
               "Time formats: yyyy-MM-dd HH:mm, tomorrow HH:mm, in 30m, in 2h";
    }
}
