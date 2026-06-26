using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class AgentTaskService
{
    public async Task<AgentTask> CreatePlanAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        string goal,
        CancellationToken cancellationToken)
    {
        goal = NormalizeGoal(goal);
        var task = new AgentTask
        {
            ConnectedUserId = user.Id,
            ChatId = user.ChatId,
            Goal = goal,
            Status = AgentTaskStatuses.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Steps = BuildPlanSteps(goal)
                .Select((description, index) => new AgentTaskStep
                {
                    StepNumber = index + 1,
                    Description = description,
                    CreatedAt = DateTime.UtcNow
                })
                .ToList()
        };

        dbContext.AgentTasks.Add(task);
        await dbContext.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task<IReadOnlyList<AgentTask>> ListAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        bool includeDone,
        CancellationToken cancellationToken)
    {
        IQueryable<AgentTask> query = dbContext.AgentTasks
            .Include(x => x.Steps)
            .Where(x => x.ConnectedUserId == user.Id);

        if (!includeDone)
        {
            query = query.Where(x => x.Status == AgentTaskStatuses.Active);
        }

        return await query
            .OrderByDescending(x => x.UpdatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
    }

    public async Task<AgentTask?> GetAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int taskId,
        CancellationToken cancellationToken)
    {
        return await dbContext.AgentTasks
            .Include(x => x.Steps.OrderBy(step => step.StepNumber))
            .FirstOrDefaultAsync(x => x.Id == taskId && x.ConnectedUserId == user.Id, cancellationToken);
    }

    public async Task<TaskUpdateResult> MarkDoneAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int taskId,
        int? stepNumber,
        CancellationToken cancellationToken)
    {
        AgentTask? task = await GetAsync(dbContext, user, taskId, cancellationToken);
        if (task is null)
        {
            return new TaskUpdateResult(false, $"Task #{taskId} was not found.", null);
        }

        if (task.Status != AgentTaskStatuses.Active)
        {
            return new TaskUpdateResult(false, $"Task #{task.Id} is already {task.Status}.", task);
        }

        if (stepNumber is null)
        {
            foreach (AgentTaskStep step in task.Steps)
            {
                MarkStepDone(step);
            }

            task.Status = AgentTaskStatuses.Completed;
            task.CompletedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new TaskUpdateResult(true, $"Task #{task.Id} marked completed.", task);
        }

        AgentTaskStep? selectedStep = task.Steps.FirstOrDefault(x => x.StepNumber == stepNumber.Value);
        if (selectedStep is null)
        {
            return new TaskUpdateResult(false, $"Task #{task.Id} does not have step {stepNumber}.", task);
        }

        MarkStepDone(selectedStep);
        task.UpdatedAt = DateTime.UtcNow;

        if (task.Steps.All(x => x.IsDone))
        {
            task.Status = AgentTaskStatuses.Completed;
            task.CompletedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new TaskUpdateResult(true, $"Task #{task.Id} step {selectedStep.StepNumber} marked done.", task);
    }

    public async Task<TaskUpdateResult> CancelAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int taskId,
        CancellationToken cancellationToken)
    {
        AgentTask? task = await GetAsync(dbContext, user, taskId, cancellationToken);
        if (task is null)
        {
            return new TaskUpdateResult(false, $"Task #{taskId} was not found.", null);
        }

        if (task.Status != AgentTaskStatuses.Active)
        {
            return new TaskUpdateResult(false, $"Task #{task.Id} is already {task.Status}.", task);
        }

        task.Status = AgentTaskStatuses.Cancelled;
        task.UpdatedAt = DateTime.UtcNow;
        task.CompletedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new TaskUpdateResult(true, $"Task #{task.Id} cancelled.", task);
    }

    public static string RenderTask(AgentTask task)
    {
        int done = task.Steps.Count(x => x.IsDone);
        int total = task.Steps.Count;
        string steps = total == 0
            ? "No steps."
            : string.Join("\n", task.Steps.OrderBy(x => x.StepNumber).Select(RenderTaskStep));

        return $"Task #{task.Id} [{task.Status}] {done}/{total} done\nGoal: {task.Goal}\n{steps}";
    }

    private static string RenderTaskStep(AgentTaskStep step)
    {
        string line = $"{(step.IsDone ? "[x]" : "[ ]")} {step.StepNumber}. {step.Description}";
        List<string> scheduleParts = [];

        if (step.ScheduledAtUtc is DateTime scheduledAtUtc)
        {
            scheduleParts.Add($"scheduled {FormatUtc(scheduledAtUtc)}");
        }

        if (step.ReminderSentAtUtc is DateTime reminderSentAtUtc)
        {
            scheduleParts.Add($"reminded {FormatUtc(reminderSentAtUtc)}");
        }

        if (!string.IsNullOrWhiteSpace(step.ScheduleNote))
        {
            scheduleParts.Add(step.ScheduleNote.Trim());
        }

        return scheduleParts.Count == 0
            ? line
            : $"{line} ({string.Join("; ", scheduleParts)})";
    }

    private static string FormatUtc(DateTime value)
    {
        DateTime utcValue = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utcValue.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }

    private static void MarkStepDone(AgentTaskStep step)
    {
        step.IsDone = true;
        step.CompletedAt ??= DateTime.UtcNow;
    }

    private static string NormalizeGoal(string goal)
    {
        goal = string.Join(' ', goal.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        if (goal.Length > 500)
        {
            goal = goal[..500];
        }

        return goal;
    }

    private static IReadOnlyList<string> BuildPlanSteps(string goal)
    {
        if (goal.Contains("project", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("app", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("api", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("bot", StringComparison.OrdinalIgnoreCase))
        {
            return [
                "Clarify the goal, users, inputs, outputs, and success criteria.",
                "Break the work into small features and decide the data model.",
                "Implement the first safe version with clear structure.",
                "Test the main flows and fix errors.",
                "Build/release the working version and document how to run it."
            ];
        }

        if (goal.Contains("study", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("learn", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("exam", StringComparison.OrdinalIgnoreCase))
        {
            return [
                "Define the exact topic and target outcome.",
                "Collect the core concepts and examples.",
                "Practice with small exercises or questions.",
                "Review weak points and summarize the key notes.",
                "Do a final check with a mini quiz or practical task."
            ];
        }

        return [
            "Clarify the target outcome and constraints.",
            "Gather the required information or resources.",
            "Do the first concrete action toward the goal.",
            "Verify the result and adjust if needed.",
            "Finish, document the outcome, and decide the next step."
        ];
    }
}

public sealed record TaskUpdateResult(bool Success, string Message, AgentTask? Task);
