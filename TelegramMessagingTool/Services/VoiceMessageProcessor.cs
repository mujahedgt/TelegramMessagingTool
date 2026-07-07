using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.Services;

public sealed record VoiceMessageProcessResult(
    string ReplyText,
    UploadedFile? ReplyAudioFile = null,
    bool SendReplyAudioAsVoice = false);

public sealed class VoiceMessageProcessor
{
    private const int MaxTtsCharacters = 4000;
    private readonly BotSettings _settings;
    private readonly DocumentStorageService _documentStorage;
    private readonly ToolRegistry _toolRegistry;
    private readonly AgentRunner _agentRunner;
    private readonly ConversationService _conversationService;
    private readonly IAudioTranscriptionService? _audioTranscriptionService;
    private readonly ITextToSpeechService? _textToSpeechService;

    public VoiceMessageProcessor(
        BotSettings settings,
        DocumentStorageService documentStorage,
        ToolRegistry toolRegistry,
        AgentRunner agentRunner,
        ConversationService conversationService,
        IAudioTranscriptionService? audioTranscriptionService,
        ITextToSpeechService? textToSpeechService)
    {
        _settings = settings;
        _documentStorage = documentStorage;
        _toolRegistry = toolRegistry;
        _agentRunner = agentRunner;
        _conversationService = conversationService;
        _audioTranscriptionService = audioTranscriptionService;
        _textToSpeechService = textToSpeechService;
    }

    public async Task<VoiceMessageProcessResult> ProcessAsync(
        UploadedFile voiceFile,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!_settings.EnableAudioTranscription)
        {
            return new VoiceMessageProcessResult("Voice message saved, but audio transcription is disabled. Configure ENABLE_AUDIO_TRANSCRIPTION=true with AUDIO_TRANSCRIPTION_COMMAND to let me understand Telegram voice messages.");
        }

        if (_audioTranscriptionService is null)
        {
            return new VoiceMessageProcessResult("Voice message saved, but no trusted local transcription provider is configured. Set AUDIO_TRANSCRIPTION_COMMAND and AUDIO_TRANSCRIPTION_ARGUMENTS first.");
        }

        AudioTranscriptionResult transcription = await _audioTranscriptionService.TranscribeAsync(voiceFile, cancellationToken);
        if (!transcription.Success)
        {
            return new VoiceMessageProcessResult($"Voice message saved as #{voiceFile.Id}, but transcription failed:\n{transcription.Output}");
        }

        string transcript = transcription.Output.Trim();
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return new VoiceMessageProcessResult($"Voice message saved as #{voiceFile.Id}, but the transcription provider returned an empty transcript.");
        }

        UploadedFile transcriptFile = await _documentStorage.CreateTextFileAsync(
            user,
            $"{voiceFile.OriginalFileName}-transcript.txt",
            transcript,
            cancellationToken);
        transcriptFile.Source = "transcript";
        dbContext.UploadedFiles.Add(transcriptFile);

        dbContext.Messages.Add(new ChatMessage
        {
            ConnectedUserId = user.Id,
            ChatId = user.ChatId,
            Content = "[Voice message transcript]\n" + transcript,
            Role = ChatRoles.User,
            Timestamp = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        List<OllamaMessageDto> conversationContext = await _conversationService.CreateConversationContextAsync(
            dbContext,
            user.Id,
            maxHistory: _settings.ConversationMaxHistory,
            cancellationToken: cancellationToken,
            toolInstructions: _toolRegistry.RenderToolInstructions());

        string finalAnswer = await _agentRunner.RunAsync(
            conversationContext,
            cancellationToken,
            dbContext,
            user);

        dbContext.Messages.Add(new ChatMessage
        {
            ConnectedUserId = user.Id,
            ChatId = user.ChatId,
            Content = finalAnswer,
            Role = ChatRoles.Assistant,
            Timestamp = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!_settings.EnableTextToSpeech || _textToSpeechService is null)
        {
            return new VoiceMessageProcessResult(finalAnswer + "\n\nVoice reply is not configured yet. Set ENABLE_TEXT_TO_SPEECH=true with TEXT_TO_SPEECH_COMMAND to receive spoken replies.");
        }

        string ttsText = finalAnswer.Length <= MaxTtsCharacters ? finalAnswer : finalAnswer[..MaxTtsCharacters];
        TextToSpeechResult speech = await _textToSpeechService.SynthesizeAsync(ttsText, cancellationToken);
        if (!speech.Success || speech.AudioBytes.Length == 0)
        {
            return new VoiceMessageProcessResult(finalAnswer + $"\n\nText-to-speech failed:\n{speech.Message}");
        }

        string extension = BotConfiguration.NormalizeTextToSpeechOutputExtension(speech.OutputExtension);
        string fileName = $"voice-reply-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{extension}";
        await using var audioStream = new MemoryStream(speech.AudioBytes, writable: false);
        UploadedFile replyAudio = await _documentStorage.SaveUploadedFileAsync(
            user,
            fileName,
            telegramFileId: string.Empty,
            contentType: GetAudioContentType(extension),
            audioStream,
            speech.AudioBytes.LongLength,
            cancellationToken);
        replyAudio.Source = "tts_voice_reply";
        dbContext.UploadedFiles.Add(replyAudio);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new VoiceMessageProcessResult(finalAnswer, replyAudio, IsTelegramVoiceExtension(extension));
    }

    public static bool IsTelegramVoiceExtension(string extension)
    {
        string normalized = BotConfiguration.NormalizeTextToSpeechOutputExtension(extension);
        return normalized is ".ogg" or ".oga" or ".opus";
    }

    public static string GetAudioContentType(string extension)
    {
        return BotConfiguration.NormalizeTextToSpeechOutputExtension(extension).ToLowerInvariant() switch
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
