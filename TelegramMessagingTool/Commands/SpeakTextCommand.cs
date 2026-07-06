using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class SpeakTextCommand : IBotCommand
{
    private const int MaxSpeakTextCharacters = 4000;
    private readonly BotSettings _settings;
    private readonly DocumentStorageService _documentStorage;
    private readonly ITextToSpeechService? _textToSpeechService;

    public SpeakTextCommand(
        BotSettings settings,
        DocumentStorageService documentStorage,
        ITextToSpeechService? textToSpeechService = null)
    {
        _settings = settings;
        _documentStorage = documentStorage;
        _textToSpeechService = textToSpeechService;
    }

    public string Name => "/speaktext";

    public string Description => "Generate TTS audio into the sandbox without sending it automatically.";

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

        string text = CommandParser.GetArguments(messageText, Name).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CommandResult(true, "Usage: /speaktext <text to synthesize>");
        }

        if (text.Length > MaxSpeakTextCharacters)
        {
            return new CommandResult(true, $"Text is too long for TTS. Maximum allowed length is {MaxSpeakTextCharacters} characters.");
        }

        if (!_settings.EnableTextToSpeech)
        {
            return new CommandResult(true, "Text-to-speech is disabled. Set ENABLE_TEXT_TO_SPEECH=true only after configuring a trusted local TTS provider. No audio was generated or sent.");
        }

        if (_textToSpeechService is null)
        {
            return new CommandResult(true, "Text-to-speech is enabled, but no trusted local TTS provider is configured. Set TEXT_TO_SPEECH_COMMAND before using /speaktext.");
        }

        TextToSpeechResult result = await _textToSpeechService.SynthesizeAsync(text, cancellationToken);
        if (!result.Success)
        {
            return new CommandResult(true, $"Text-to-speech failed:\n{result.Message}");
        }

        if (result.AudioBytes.Length == 0)
        {
            return new CommandResult(true, "Text-to-speech failed: provider returned empty audio output.");
        }

        string extension = BotConfiguration.NormalizeTextToSpeechOutputExtension(result.OutputExtension);
        string fileName = $"tts-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{extension}";
        await using var audioStream = new MemoryStream(result.AudioBytes, writable: false);
        UploadedFile ttsFile = await _documentStorage.SaveUploadedFileAsync(
            user,
            fileName,
            telegramFileId: string.Empty,
            contentType: GetAudioContentType(extension),
            audioStream,
            result.AudioBytes.LongLength,
            cancellationToken);
        ttsFile.Source = "tts";
        dbContext.UploadedFiles.Add(ttsFile);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CommandResult(true, $"Saved TTS audio file: #{ttsFile.Id} {ttsFile.OriginalFileName}\nThe audio was stored in the sandbox and was not sent automatically. Use /voicefiles to list generated audio files.");
    }

    private static string GetAudioContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".ogg" or ".oga" => "audio/ogg",
            ".opus" => "audio/opus",
            ".flac" => "audio/flac",
            _ => "audio/mpeg"
        };
    }
}
