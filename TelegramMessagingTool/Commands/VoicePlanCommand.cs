using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class VoicePlanCommand : IBotCommand
{
    private readonly BotSettings _settings;
    private readonly DocumentStorageService _documentStorage;
    private readonly IAudioTranscriptionService? _audioTranscriptionService;
    private readonly TranscriptInsightsService _transcriptInsightsService;

    public VoicePlanCommand(
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

    public string Name => "/voiceplan";

    public string Description => "Transcribe a saved audio file and draft a review-only task plan from it.";

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

        string taskDraft = await _transcriptInsightsService.GenerateTaskDraftAsync(
            preparation.TranscriptText,
            $"Audio #{preparation.AudioFile!.Id} {preparation.AudioFile.OriginalFileName}",
            cancellationToken);

        return new CommandResult(true, $"Voice plan draft for #{preparation.AudioFile.Id} {preparation.AudioFile.OriginalFileName}:\n\n{taskDraft}\n\nSaved transcript file: #{preparation.TranscriptFile!.Id} {preparation.TranscriptFile.OriginalFileName}\nReview this draft before running the suggested /plan command. No task was created automatically.");
    }
}
