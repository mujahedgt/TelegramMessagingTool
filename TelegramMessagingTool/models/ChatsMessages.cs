namespace TelegramMessagingTool.Models;

public class ChatMessage
{
    public int Id { get; set; }

    public int ConnectedUserId { get; set; }

    public long ChatId { get; set; }

    public ChatRoles Role { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public ConnectedUser User { get; set; } = null!;
}