namespace TelegramMessagingTool.Models;

public class DocumentChunk
{
    public int Id { get; set; }

    public int UploadedFileId { get; set; }

    public int ConnectedUserId { get; set; }

    public long ChatId { get; set; }

    public int ChunkNumber { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public int CharacterCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public UploadedFile UploadedFile { get; set; } = null!;

    public ConnectedUser User { get; set; } = null!;
}
