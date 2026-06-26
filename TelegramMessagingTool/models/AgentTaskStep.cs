namespace TelegramMessagingTool.Models;

public class AgentTaskStep
{
    public int Id { get; set; }

    public int AgentTaskId { get; set; }

    public int StepNumber { get; set; }

    public string Description { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public DateTime? ScheduledAtUtc { get; set; }

    public DateTime? ReminderSentAtUtc { get; set; }

    public string? ScheduleNote { get; set; }

    public AgentTask AgentTask { get; set; } = null!;
}
