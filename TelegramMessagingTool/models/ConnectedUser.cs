using TelegramMessagingTool.models;

namespace TelegramMessagingTool.Models;

public class ConnectedUser
{
    public int Id { get; set; }

    public long ChatId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

    public override string ToString()
    {
        return $"ChatId: {ChatId}, Username: {Name}, Name: {FirstName} {LastName}, Messages: {Messages.Count}";
    }
}