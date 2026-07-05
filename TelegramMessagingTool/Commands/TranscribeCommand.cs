using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class TranscribeCommand : IBotCommand
{
    private readonly BotSettings _settings;
    private readonly DocumentStorageService _documentStorage;
    private readonly IAudioTranscriptionService? _audioTranscriptionService;

    public TranscribeCommand(
        BotSettings settings,
        DocumentStorageService documentStorage,
        IAudioTranscriptionService? audioTranscriptionService = null)
    {
        _settings = settings;
        _documentStorage = documentStorage;
        _audioTranscriptionService = audioTranscriptionService;
    }

    public string Name => "/transcribe";

    public string Description => "Show metadata for a saved audio file and optionally transcribe it with a configured local provider.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        string idText = CommandParser.GetArguments(messageText, Name);
        if (!int.TryParse(idText, out int audioFileId))
        {
            return new CommandResult(true, "Usage: /transcribe <audio-file-id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == audioFileId && x.ConnectedUserId == user.Id, cancellationToken);

        if (file is null)
        {
            return new CommandResult(true, $"Audio file #{audioFileId} was not found.");
        }

        if (!DocumentStorageService.IsAudioFileName(file.OriginalFileName)
            && !file.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(true, $"File #{file.Id} is not an audio file. Use /files to list saved files or /voicefiles to list saved audio files.");
        }

        string absolutePath = Path.GetFullPath(file.AbsolutePath);
        bool insideSandbox = IsInsideSandbox(absolutePath);
        string diskStatus = File.Exists(absolutePath) ? "present" : "missing";
        string reply = $"""
Audio #{file.Id}: {file.OriginalFileName}

Metadata:
- Content type: {file.ContentType}
- Size: {file.SizeBytes} bytes
- Source: {file.Source}
- Disk: {diskStatus}
- Voice model route: {_settings.OllamaVoiceModel}
- Audio transcription: {(_settings.EnableAudioTranscription ? "enabled" : "disabled")}
""";

        if (!insideSandbox)
        {
            return new CommandResult(true, reply + "\n\nAudio file is outside the current document sandbox. Re-upload or import it again before transcription.");
        }

        if (!_settings.EnableAudioTranscription)
        {
            return new CommandResult(true, reply + "\n\nAudio transcription is disabled. Set ENABLE_AUDIO_TRANSCRIPTION=true only after configuring a trusted local transcription provider.");
        }

        if (_audioTranscriptionService is null)
        {
            return new CommandResult(true, reply + "\n\nAudio transcription is enabled, but no transcription provider is configured yet. Local Whisper/provider integration is planned next.");
        }

        AudioTranscriptionResult transcription = await _audioTranscriptionService.TranscribeAsync(file, cancellationToken);
        string label = transcription.Success ? "Transcript" : "Transcription failed";
        if (!transcription.Success)
        {
            return new CommandResult(true, reply + $"\n\n{label}:\n{transcription.Output}");
        }

        UploadedFile transcriptFile;
        try
        {
            string transcriptFileName = $"{file.OriginalFileName}-transcript.txt";
            transcriptFile = await _documentStorage.CreateTextFileAsync(user, transcriptFileName, transcription.Output, cancellationToken);
            transcriptFile.Source = "transcript";
            dbContext.UploadedFiles.Add(transcriptFile);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new CommandResult(true, reply + $"\n\n{label}:\n{transcription.Output}\n\nTranscript was generated but could not be saved as a document: {ex.Message}");
        }

        return new CommandResult(true, reply + $"\n\n{label}:\n{transcription.Output}\n\nSaved transcript file: #{transcriptFile.Id} {transcriptFile.OriginalFileName}\nUse /readfile {transcriptFile.Id}, /indexfile {transcriptFile.Id}, or /askfile {transcriptFile.Id} <question>.");
    }

    private bool IsInsideSandbox(string absolutePath)
    {
        string root = Path.GetFullPath(_documentStorage.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return absolutePath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
