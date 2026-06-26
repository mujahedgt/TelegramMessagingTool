using System.Globalization;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ScheduleListCommand : IBotCommand
{
    private readonly AgentTaskService _agentTaskService;

    public ScheduleListCommand(AgentTaskService agentTaskService)
    {
        _agentTaskService = agentTaskService;
    }

    public string Name => "/schedulelist";
    public string Description => "List scheduled task step reminders.";

    public async Task<CommandResult> TryHandleAsync(Message message, ConnectedUser user, TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        IReadOnlyList<ScheduledTaskStep> scheduledSteps = await _agentTaskService.ListScheduledStepsAsync(dbContext, user, cancellationToken);
        if (scheduledSteps.Count == 0)
        {
            return new CommandResult(true, "No scheduled task steps. Use /schedule <task-id> <step-number> <time> [note] to schedule one.");
        }

        string reply = string.Join("\n", scheduledSteps.Select(item =>
        {
            AgentTask task = item.Task;
            AgentTaskStep step = item.Step;
            string scheduledAt = FormatUtc(step.ScheduledAtUtc!.Value);
            string note = string.IsNullOrWhiteSpace(step.ScheduleNote) ? string.Empty : $" - {step.ScheduleNote.Trim()}";
            string reminded = step.ReminderSentAtUtc is DateTime reminderSentAtUtc
                ? $"; reminded {FormatUtc(reminderSentAtUtc)}"
                : string.Empty;
            return $"Task #{task.Id} step {step.StepNumber}: {scheduledAt}{reminded} - {step.Description}{note}";
        }));

        return new CommandResult(true, "Scheduled task steps:\n" + reply);
    }

    private static string FormatUtc(DateTime value)
    {
        DateTime utcValue = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utcValue.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }
}
