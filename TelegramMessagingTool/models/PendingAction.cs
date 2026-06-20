namespace TelegramMessagingTool.Models;

public class PendingAction
{
    public int Id { get; set; }

    public int ConnectedUserId { get; set; }

    public long ChatId { get; set; }

    public string ToolName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public string RiskLevel { get; set; } = "medium";

    public string Status { get; set; } = PendingActionStatuses.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(30);

    public DateTime? DecidedAt { get; set; }

    public string DecisionNote { get; set; } = string.Empty;

    public ConnectedUser User { get; set; } = null!;
}

public static class PendingActionStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Denied = "denied";
    public const string Expired = "expired";
}
