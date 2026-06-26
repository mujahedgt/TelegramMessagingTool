using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Telegram.Bot;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class TaskReminderService
{
    private const int MaxRemindersPerScan = 20;
    private readonly ITaskReminderSender _sender;
    private readonly Func<DateTime> _utcNowProvider;

    public TaskReminderService(ITaskReminderSender sender, Func<DateTime>? utcNowProvider = null)
    {
        _sender = sender;
        _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
    }

    public async Task<ReminderScanResult> SendDueRemindersAsync(
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = EnsureUtc(_utcNowProvider());
        List<AgentTaskStep> dueSteps = await dbContext.AgentTaskSteps
            .Include(x => x.AgentTask)
            .Where(x => x.AgentTask.Status == AgentTaskStatuses.Active
                && x.ScheduledAtUtc != null
                && x.ScheduledAtUtc <= nowUtc
                && x.ReminderSentAtUtc == null)
            .OrderBy(x => x.ScheduledAtUtc)
            .ThenBy(x => x.AgentTaskId)
            .ThenBy(x => x.StepNumber)
            .Take(MaxRemindersPerScan)
            .ToListAsync(cancellationToken);

        int sent = 0;
        int failed = 0;

        foreach (AgentTaskStep step in dueSteps)
        {
            string message = BuildReminderMessage(step.AgentTask, step);
            try
            {
                await _sender.SendReminderAsync(step.AgentTask.ChatId, message, cancellationToken);
                step.ReminderSentAtUtc = nowUtc;
                step.AgentTask.UpdatedAt = nowUtc;
                await dbContext.SaveChangesAsync(cancellationToken);
                sent++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                failed++;
            }
        }

        return new ReminderScanResult(dueSteps.Count, sent, failed);
    }

    public static string BuildReminderMessage(AgentTask task, AgentTaskStep step)
    {
        string scheduledAt = step.ScheduledAtUtc is DateTime scheduledAtUtc
            ? FormatUtc(scheduledAtUtc)
            : "unscheduled";
        string note = string.IsNullOrWhiteSpace(step.ScheduleNote)
            ? string.Empty
            : $"\nNote: {step.ScheduleNote.Trim()}";

        return $"""
Task reminder
Task #{task.Id}: {task.Goal}
Step {step.StepNumber}: {step.Description}
Scheduled: {scheduledAt}{note}

Use /done {task.Id} {step.StepNumber} when finished, or /schedulelist to view scheduled steps.
""";
    }

    private static string FormatUtc(DateTime value)
    {
        DateTime utcValue = EnsureUtc(value);
        return utcValue.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}

public interface ITaskReminderSender
{
    Task SendReminderAsync(long chatId, string text, CancellationToken cancellationToken);
}

public sealed class TelegramTaskReminderSender : ITaskReminderSender
{
    private readonly ITelegramBotClient _botClient;

    public TelegramTaskReminderSender(ITelegramBotClient botClient)
    {
        _botClient = botClient;
    }

    public async Task SendReminderAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        await _botClient.SendMessage(
            chatId: chatId,
            text: text,
            cancellationToken: cancellationToken);
    }
}

public sealed record ReminderScanResult(int DueCount, int SentCount, int FailedCount);
