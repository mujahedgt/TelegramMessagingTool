using System.Text;

namespace TelegramMessagingTool.Services;

public sealed record AgentHarnessDefinition(
    string Name,
    string Purpose,
    string Status,
    IReadOnlyList<string> ImplementedGates,
    IReadOnlyList<string> LaterGatedWork,
    IReadOnlyList<string> SafetyRules);

public static class AgentHarnessCatalog
{
    public static IReadOnlyList<AgentHarnessDefinition> GetDefaultHarnesses()
    {
        return
        [
            new AgentHarnessDefinition(
                Name: "image_agent",
                Purpose: "Safe harness for sandboxed image understanding workflows without exposing arbitrary local file paths.",
                Status: "foundation active",
                ImplementedGates:
                [
                    "/images lists sandboxed uploaded image files",
                    "/describeimage <id> returns safe metadata by default",
                    "ENABLE_IMAGE_VISION gates local Ollama image-description execution",
                    "IMAGE_DESCRIPTION_PROMPT customizes the local vision prompt with a 1000-character cap"
                ],
                LaterGatedWork:
                [
                    "extract_image_text: OCR text from a sandboxed uploaded image",
                    "generate_image_prompt: turn a user idea into a structured image prompt",
                    "create_image: approval/feature-flagged image generation entry point"
                ],
                SafetyRules:
                [
                    "Accept only sandboxed uploaded image files, never arbitrary local paths.",
                    "Keep generated images and extracted text tied to the requesting chat/user.",
                    "Do not call external image APIs unless an explicit provider flag is enabled.",
                    "Require approval before deleting, overwriting, or sending generated media outside the current chat."
                ]),
            new AgentHarnessDefinition(
                Name: "voice_agent",
                Purpose: "Safe harness for sandboxed audio transcription, transcript insights, and stored text-to-speech output.",
                Status: "foundation active",
                ImplementedGates:
                [
                    "/voicefiles lists sandboxed uploaded audio files",
                    "/transcribe <id> runs only when ENABLE_AUDIO_TRANSCRIPTION and a trusted provider command are configured",
                    "Successful transcripts are saved back into the user document sandbox",
                    "/transcriptinsights <id> summarizes saved transcript text through the voice model route",
                    "/speaktext <text> runs only when ENABLE_TEXT_TO_SPEECH and a trusted provider command are configured, then stores generated audio without automatic sending"
                ],
                LaterGatedWork:
                [
                    "extract_audio_tasks: create planner tasks from transcript action items after explicit command",
                    "send_voice/send_audio: explicit delivery of stored TTS audio after ownership and type checks",
                    "richer voice workflow orchestration behind provider/resource gates"
                ],
                SafetyRules:
                [
                    "Accept only sandboxed Telegram voice/audio files, never arbitrary local paths.",
                    "Store transcripts as private per-chat artifacts with content logging disabled by default.",
                    "Do not send voice/TTS replies unless explicitly requested by the user.",
                    "Require approval before forwarding audio/transcripts outside the current chat."
                ])
        ];
    }

    public static string RenderHarnesses(IReadOnlyList<AgentHarnessDefinition> harnesses) => RenderHarnesses(settings: null, harnesses: harnesses);

    public static string RenderHarnesses(BotSettings? settings = null, IReadOnlyList<AgentHarnessDefinition>? harnesses = null)
    {
        IReadOnlyList<AgentHarnessDefinition> selectedHarnesses = harnesses ?? GetDefaultHarnesses();
        var builder = new StringBuilder();
        builder.AppendLine("Image and Voice Agent Harnesses");
        builder.AppendLine();
        builder.AppendLine("These harnesses expose current feature-gate readiness and the remaining gated work. Implemented gates are safe-by-default and stay disabled until their explicit provider flags are configured.");

        if (settings is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Harness model routes and gates:");
            builder.AppendLine($"- image_agent route: {settings.OllamaImageModel}");
            builder.AppendLine($"- voice_agent route: {settings.OllamaVoiceModel}");
            builder.AppendLine($"- image vision execution: {(settings.EnableImageVision ? "enabled" : "disabled")}");
            builder.AppendLine($"- image description prompt: {(settings.ImageDescriptionPrompt == BotConfiguration.DefaultImageDescriptionPrompt ? "default" : "custom")}");
            builder.AppendLine($"- audio transcription execution: {(settings.EnableAudioTranscription ? "enabled" : "disabled")}");
            builder.AppendLine($"- audio transcription provider: {(string.IsNullOrWhiteSpace(settings.AudioTranscriptionCommand) ? "not configured" : "local command configured")}");
            builder.AppendLine($"- text-to-speech execution: {(settings.EnableTextToSpeech ? "enabled" : "disabled")}");
            builder.AppendLine($"- text-to-speech provider: {(string.IsNullOrWhiteSpace(settings.TextToSpeechCommand) ? "not configured" : "local command configured")}");
        }

        foreach (AgentHarnessDefinition harness in selectedHarnesses)
        {
            builder.AppendLine();
            builder.AppendLine($"## {harness.Name} ({harness.Status})");
            builder.AppendLine(harness.Purpose);
            AppendList(builder, "Implemented gates", harness.ImplementedGates);
            AppendList(builder, "Later gated work", harness.LaterGatedWork);
            AppendList(builder, "Safety rules", harness.SafetyRules);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine($"{title}:");
        foreach (string value in values)
        {
            builder.AppendLine($"- {value}");
        }
    }
}
