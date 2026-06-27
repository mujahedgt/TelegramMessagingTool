using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Telegram;

namespace TelegramMessagingTool.Services;

public sealed class TaskCallbackService
{
    private readonly AgentTaskService _agentTaskService;

    public TaskCallbackService(AgentTaskService agentTaskService)
    {
        _agentTaskService = agentTaskService;
    }

    public async Task<TaskCallbackResult> HandleAsync(
        string? callbackData,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!TaskCallbackParser.TryParse(callbackData, out TaskCallback callback))
        {
            return TaskCallbackResult.NotHandled;
        }

        return callback.Verb switch
        {
            TaskCallbackVerb.Open => await OpenAsync(callback.TaskId, user, dbContext, cancellationToken),
            TaskCallbackVerb.Done => await DoneAsync(callback.TaskId, user, dbContext, cancellationToken),
            TaskCallbackVerb.DoneStep => await DoneStepAsync(callback.TaskId, callback.StepNumber, user, dbContext, cancellationToken),
            TaskCallbackVerb.Cancel => NotEnabledYet("Cancel"),
            _ => TaskCallbackResult.NotHandled
        };
    }

    private async Task<TaskCallbackResult> OpenAsync(
        int taskId,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        AgentTask? task = await _agentTaskService.GetAsync(dbContext, user, taskId, cancellationToken);
        if (task is null)
        {
            return new TaskCallbackResult(true, "Not found", $"Task #{taskId} was not found.");
        }

        return new TaskCallbackResult(true, "Opened task", AgentTaskService.RenderTask(task));
    }

    private async Task<TaskCallbackResult> DoneAsync(
        int taskId,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        TaskUpdateResult result = await _agentTaskService.MarkDoneAsync(dbContext, user, taskId, stepNumber: null, cancellationToken);
        string details = result.Task is null ? string.Empty : "\n\n" + AgentTaskService.RenderTask(result.Task);
        string answerText = result.Success ? "Done" : "Done failed";
        return new TaskCallbackResult(true, answerText, result.Message + details);
    }

    private async Task<TaskCallbackResult> DoneStepAsync(
        int taskId,
        int? stepNumber,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (stepNumber is null)
        {
            return new TaskCallbackResult(true, "Invalid step", $"Task #{taskId} step number was not provided.");
        }

        TaskUpdateResult result = await _agentTaskService.MarkDoneAsync(dbContext, user, taskId, stepNumber, cancellationToken);
        string details = result.Task is null ? string.Empty : "\n\n" + AgentTaskService.RenderTask(result.Task);
        string answerText = result.Success ? "Done" : "Done failed";
        return new TaskCallbackResult(true, answerText, result.Message + details);
    }

    private static TaskCallbackResult NotEnabledYet(string verb)
    {
        return new TaskCallbackResult(
            true,
            "Not enabled yet",
            $"{verb} task buttons are visible, but task mutation from inline buttons is not enabled yet. Use the slash command for now.");
    }
}

public sealed record TaskCallbackResult(bool Handled, string AnswerText, string? MessageText)
{
    public static TaskCallbackResult NotHandled { get; } = new(false, "Unsupported action", null);
}
