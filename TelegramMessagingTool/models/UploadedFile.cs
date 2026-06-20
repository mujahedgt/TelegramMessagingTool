namespace TelegramMessagingTool.Models;

public class UploadedFile
{
    public int Id { get; set; }

    public int ConnectedUserId { get; set; }

    public long ChatId { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string AbsolutePath { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Source { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ConnectedUser User { get; set; } = null!;
}
