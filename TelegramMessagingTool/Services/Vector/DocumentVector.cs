namespace TelegramMessagingTool.Services.Vector;

public sealed record DocumentVector(
    string Id,
    long ChatId,
    int ConnectedUserId,
    int UploadedFileId,
    int ChunkId,
    int ChunkNumber,
    string OriginalFileName,
    string Text,
    IReadOnlyList<float> Embedding);
