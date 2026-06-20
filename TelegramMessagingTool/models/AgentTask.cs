namespace TelegramMessagingTool.Models;

public class AgentTask
{
    public int Id { get; set; }

    public int ConnectedUserId { get; set; }

    public long ChatId { get; set; }

    public string Goal { get; set; } = string.Empty;

    public string Status { get; set; } = AgentTaskStatuses.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public ConnectedUser User { get; set; } = null!;

    public List<AgentTaskStep> Steps { get; set; } = [];
}

public static class AgentTaskStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}
