using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed record AudioTranscriptionResult(bool Success, string Output);

public interface IAudioTranscriptionService
{
    Task<AudioTranscriptionResult> TranscribeAsync(
        UploadedFile audioFile,
        CancellationToken cancellationToken);
}
