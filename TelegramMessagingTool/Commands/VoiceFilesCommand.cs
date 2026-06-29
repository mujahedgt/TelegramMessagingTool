using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class VoiceFilesCommand : IBotCommand
{
    public string Name => "/voicefiles";

    public string Description => "List saved sandboxed audio files for the planned voice-agent harness.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!CommandParser.Matches(message.Text, Name))
        {
            return new CommandResult(false, null);
        }

        List<UploadedFile> audioFiles = await dbContext.UploadedFiles
            .Where(x => x.ConnectedUserId == user.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        audioFiles = audioFiles
            .Where(x => DocumentStorageService.IsAudioFileName(x.OriginalFileName)
                || x.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();

        if (audioFiles.Count == 0)
        {
            return new CommandResult(true, "No audio files saved yet. Upload a .mp3/.wav/.m4a/.ogg/.oga/.opus/.flac file as a Telegram document, then use /voicefiles again. Transcription and voice-agent summarization are planned next but not implemented yet.");
        }

        string reply = "Saved audio files for voice_agent harness:\n"
            + string.Join("\n", audioFiles.Select(x => $"#{x.Id}: {x.OriginalFileName} ({x.SizeBytes} bytes, {x.Source})"))
            + "\n\nNext planned commands: /transcribe <id>, /summarizeaudio <id>, and /audio-tasks <id>.";

        return new CommandResult(true, reply);
    }
}
