namespace TelegramMessagingTool.Models;

public class Memory
{
    public int Id { get; set; }

    public int ConnectedUserId { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ConnectedUser User { get; set; } = null!;
}
