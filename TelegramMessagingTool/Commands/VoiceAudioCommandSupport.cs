using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

internal sealed record VoiceAudioCommandPreparation(
    bool Success,
    string Message,
    UploadedFile? AudioFile = null,
    UploadedFile? TranscriptFile = null,
    string TranscriptText = "")
{
    public static VoiceAudioCommandPreparation Failed(string message) => new(false, message);

    public static VoiceAudioCommandPreparation Ok(
        UploadedFile audioFile,
        UploadedFile transcriptFile,
        string transcriptText) => new(true, string.Empty, audioFile, transcriptFile, transcriptText);
}

internal static class VoiceAudioCommandSupport
{
    public static async Task<VoiceAudioCommandPreparation> TranscribeAndSaveAsync(
        string commandName,
        string messageText,
        BotSettings settings,
        DocumentStorageService documentStorage,
        IAudioTranscriptionService? audioTranscriptionService,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string idText = CommandParser.GetArguments(messageText, commandName);
        if (!int.TryParse(idText, out int audioFileId))
        {
            return VoiceAudioCommandPreparation.Failed($"Usage: {commandName} <audio-file-id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == audioFileId && x.ConnectedUserId == user.Id, cancellationToken);

        if (file is null)
        {
            return VoiceAudioCommandPreparation.Failed($"Audio file #{audioFileId} was not found.");
        }

        if (!DocumentStorageService.IsAudioFileName(file.OriginalFileName)
            && !file.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return VoiceAudioCommandPreparation.Failed($"File #{file.Id} is not an audio file. Use /voicefiles to list saved audio files.");
        }

        string absolutePath = Path.GetFullPath(file.AbsolutePath);
        if (!IsInsideSandbox(documentStorage.RootDirectory, absolutePath))
        {
            return VoiceAudioCommandPreparation.Failed("Audio file is outside the current document sandbox. Re-upload or import it again before voice-agent analysis.");
        }

        if (!File.Exists(absolutePath))
        {
            return VoiceAudioCommandPreparation.Failed("Audio file is missing on disk. Re-upload or import it again before voice-agent analysis.");
        }

        if (!settings.EnableAudioTranscription)
        {
            return VoiceAudioCommandPreparation.Failed("Audio transcription is disabled. Set ENABLE_AUDIO_TRANSCRIPTION=true only after configuring a trusted local transcription provider.");
        }

        if (audioTranscriptionService is null)
        {
            return VoiceAudioCommandPreparation.Failed("Audio transcription is enabled, but no trusted local transcription provider is configured yet.");
        }

        AudioTranscriptionResult transcription = await audioTranscriptionService.TranscribeAsync(file, cancellationToken);
        if (!transcription.Success)
        {
            return VoiceAudioCommandPreparation.Failed($"Transcription failed:\n{transcription.Output}");
        }

        string transcriptText = transcription.Output.Trim();
        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            return VoiceAudioCommandPreparation.Failed("The transcription provider returned an empty transcript.");
        }

        UploadedFile transcriptFile;
        try
        {
            transcriptFile = await documentStorage.CreateTextFileAsync(
                user,
                $"{file.OriginalFileName}-transcript.txt",
                transcriptText,
                cancellationToken);
            transcriptFile.Source = "transcript";
            dbContext.UploadedFiles.Add(transcriptFile);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return VoiceAudioCommandPreparation.Failed($"Transcript was generated but could not be saved as a document: {ex.Message}");
        }

        return VoiceAudioCommandPreparation.Ok(file, transcriptFile, transcriptText);
    }

    private static bool IsInsideSandbox(string rootDirectory, string absolutePath)
    {
        string root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return absolutePath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
