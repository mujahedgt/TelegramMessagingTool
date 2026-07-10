using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class VoiceBriefCommand : IBotCommand
{
    private readonly BotSettings _settings;
    private readonly DocumentStorageService _documentStorage;
    private readonly IAudioTranscriptionService? _audioTranscriptionService;
    private readonly TranscriptInsightsService _transcriptInsightsService;

    public VoiceBriefCommand(
        BotSettings settings,
        DocumentStorageService documentStorage,
        IAudioTranscriptionService? audioTranscriptionService,
        TranscriptInsightsService transcriptInsightsService)
    {
        _settings = settings;
        _documentStorage = documentStorage;
        _audioTranscriptionService = audioTranscriptionService;
        _transcriptInsightsService = transcriptInsightsService;
    }

    public string Name => "/voicebrief";

    public string Description => "Transcribe a saved audio file, save the transcript, and return a concise voice brief.";

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

        VoiceAudioCommandPreparation preparation = await VoiceAudioCommandSupport.TranscribeAndSaveAsync(
            Name,
            messageText,
            _settings,
            _documentStorage,
            _audioTranscriptionService,
            user,
            dbContext,
            cancellationToken);

        if (!preparation.Success)
        {
            return new CommandResult(true, preparation.Message);
        }

        string insights = await _transcriptInsightsService.GenerateAsync(
            preparation.TranscriptText,
            $"Audio #{preparation.AudioFile!.Id} {preparation.AudioFile.OriginalFileName}",
            cancellationToken);

        return new CommandResult(true, $"Voice brief for #{preparation.AudioFile.Id} {preparation.AudioFile.OriginalFileName}:\n\n{insights}\n\nSaved transcript file: #{preparation.TranscriptFile!.Id} {preparation.TranscriptFile.OriginalFileName}\nUse /readfile {preparation.TranscriptFile.Id}, /transcriptinsights {preparation.TranscriptFile.Id}, or /transcripttasks {preparation.TranscriptFile.Id} for follow-up.");
    }
}
