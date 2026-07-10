using System.Text;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class MediaProvidersCommand : IBotCommand
{
    private readonly BotSettings _settings;

    public MediaProvidersCommand(BotSettings settings)
    {
        _settings = settings;
    }

    public string Name => "/providers";

    public string Description => "Admin-only: show secret-safe local media provider diagnostics and setup examples.";

    public Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!CommandParser.Matches(message.Text, Name))
        {
            return Task.FromResult(new CommandResult(false, null));
        }

        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return Task.FromResult(new CommandResult(true, BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId)));
        }

        return Task.FromResult(new CommandResult(true, RenderSummary(_settings)));
    }

    public static string RenderSummary(BotSettings settings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Media provider diagnostics");
        builder.AppendLine();
        builder.AppendLine("Secret-safe status:");
        builder.AppendLine($"- Image vision: {RenderGate(settings.EnableImageVision, providerConfigured: true)}; route {settings.OllamaImageModel}; prompt {(settings.ImageDescriptionPrompt == BotConfiguration.DefaultImageDescriptionPrompt ? "default" : "custom")}");
        builder.AppendLine($"- Image OCR: {RenderGate(settings.EnableImageOcr, !string.IsNullOrWhiteSpace(settings.ImageOcrCommand))}; timeout {settings.ImageOcrTimeoutSeconds}s");
        builder.AppendLine($"- Audio transcription: {RenderGate(settings.EnableAudioTranscription, !string.IsNullOrWhiteSpace(settings.AudioTranscriptionCommand))}; timeout {settings.AudioTranscriptionTimeoutSeconds}s");
        builder.AppendLine($"- Text-to-speech: {RenderGate(settings.EnableTextToSpeech, !string.IsNullOrWhiteSpace(settings.TextToSpeechCommand))}; timeout {settings.TextToSpeechTimeoutSeconds}s; output {settings.TextToSpeechOutputExtension}");
        builder.AppendLine();
        builder.AppendLine("Provider contracts:");
        builder.AppendLine("- OCR provider: receives one sandboxed image path at {file}, exits 0, writes extracted text to stdout.");
        builder.AppendLine("- STT provider: receives one sandboxed audio path at {file}, exits 0, writes transcript text to stdout.");
        builder.AppendLine("- TTS provider: receives text at {text} and an output path at {output}, exits 0, creates that audio file.");
        builder.AppendLine();
        builder.AppendLine("Windows User env examples:");
        builder.AppendLine("- ENABLE_IMAGE_OCR=true; IMAGE_OCR_COMMAND=C:\\Tools\\ocr\\ocr-image.cmd; IMAGE_OCR_ARGUMENTS=\"{file}\"");
        builder.AppendLine("- ENABLE_AUDIO_TRANSCRIPTION=true; AUDIO_TRANSCRIPTION_COMMAND=C:\\Tools\\voice\\transcribe-voice.cmd; AUDIO_TRANSCRIPTION_ARGUMENTS=\"{file}\"");
        builder.AppendLine("- ENABLE_TEXT_TO_SPEECH=true; TEXT_TO_SPEECH_COMMAND=C:\\Tools\\voice\\tts-reply.cmd; TEXT_TO_SPEECH_ARGUMENTS=\"{text}\" \"{output}\"");
        builder.AppendLine();
        builder.AppendLine("Safety notes:");
        builder.AppendLine("- Commands run directly with UseShellExecute=false; values are not shell command strings.");
        builder.AppendLine("- Keep providers local, trusted, and wrapper-based. Do not put tokens or secrets in provider variables.");
        builder.AppendLine("- Restart the latest release after changing Windows User environment variables.");
        builder.AppendLine();
        builder.AppendLine("Secrets: command paths, tokens, connection strings, and provider credentials are intentionally not shown.");
        return builder.ToString().TrimEnd();
    }

    private static string RenderGate(bool enabled, bool providerConfigured)
    {
        if (!enabled)
        {
            return "disabled";
        }

        return providerConfigured ? "enabled, provider configured" : "enabled, provider missing";
    }
}
