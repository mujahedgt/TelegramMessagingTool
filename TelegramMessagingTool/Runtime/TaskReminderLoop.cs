using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Runtime;

public sealed class TaskReminderLoop
{
    private readonly TaskReminderService _taskReminderService;
    private readonly Action<string, string, string, ConsoleEventLevel> _writeConsoleEvent;
    private readonly TimeSpan _interval;

    public TaskReminderLoop(
        TaskReminderService taskReminderService,
        Action<string, string, string, ConsoleEventLevel> writeConsoleEvent,
        TimeSpan? interval = null)
    {
        _taskReminderService = taskReminderService;
        _writeConsoleEvent = writeConsoleEvent;
        _interval = interval ?? TimeSpan.FromSeconds(60);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using TelegramDbContext dbContext = new();
                ReminderScanResult result = await _taskReminderService.SendDueRemindersAsync(dbContext, cancellationToken);
                if (result.SentCount > 0 || result.FailedCount > 0)
                {
                    _writeConsoleEvent(
                        "REMINDER",
                        "tasks",
                        $"due={result.DueCount} sent={result.SentCount} failed={result.FailedCount}",
                        result.FailedCount > 0 ? ConsoleEventLevel.Warning : ConsoleEventLevel.Success);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _writeConsoleEvent("REMINDER", "tasks", ex.Message, ConsoleEventLevel.Error);
            }

            try
            {
                await Task.Delay(_interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
