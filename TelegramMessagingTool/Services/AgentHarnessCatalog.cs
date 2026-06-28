using System.Text;

namespace TelegramMessagingTool.Services;

public sealed record AgentHarnessDefinition(
    string Name,
    string Purpose,
    string Status,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> SafetyRules,
    IReadOnlyList<string> NextImplementationSteps);

public static class AgentHarnessCatalog
{
    public static IReadOnlyList<AgentHarnessDefinition> GetDefaultHarnesses()
    {
        return
        [
            new AgentHarnessDefinition(
                Name: "image_agent",
                Purpose: "Prepare a safe harness for image understanding and image-generation workflows without exposing arbitrary file paths.",
                Status: "planned",
                Tools:
                [
                    "describe_image: summarize a sandboxed uploaded image",
                    "extract_image_text: OCR text from a sandboxed uploaded image",
                    "generate_image_prompt: turn a user idea into a structured image prompt",
                    "create_image: future approval/feature-flagged image generation entry point"
                ],
                SafetyRules:
                [
                    "Accept only sandboxed uploaded image files, never arbitrary local paths.",
                    "Keep generated images and extracted text tied to the requesting chat/user.",
                    "Do not call external image APIs unless an explicit provider flag is enabled.",
                    "Require approval before deleting, overwriting, or sending generated media outside the current chat."
                ],
                NextImplementationSteps:
                [
                    "Add image file extension allowlist and metadata classification.",
                    "Add /images and /describeimage <file-id> commands first.",
                    "Add OCR/image prompt helpers after read-only description works.",
                    "Add optional generation provider only behind a feature flag."
                ]),
            new AgentHarnessDefinition(
                Name: "voice_agent",
                Purpose: "Prepare a safe harness for voice notes, transcription, summaries, and future text-to-speech replies.",
                Status: "planned",
                Tools:
                [
                    "transcribe_audio: transcribe a sandboxed voice/audio file",
                    "summarize_audio: summarize a transcript",
                    "extract_audio_tasks: turn transcript decisions into task steps",
                    "speak_text: future feature-flagged text-to-speech reply entry point"
                ],
                SafetyRules:
                [
                    "Accept only sandboxed Telegram voice/audio files, never arbitrary local paths.",
                    "Store transcripts as private per-chat artifacts with content logging disabled by default.",
                    "Do not send voice/TTS replies unless explicitly requested by the user.",
                    "Require approval before forwarding audio/transcripts outside the current chat."
                ],
                NextImplementationSteps:
                [
                    "Add voice/audio upload metadata and size/type checks.",
                    "Add /voicefiles and /transcribe <file-id> commands first.",
                    "Add transcript summary/task extraction after transcription works.",
                    "Add optional TTS provider only behind a feature flag."
                ])
        ];
    }

    public static string RenderHarnesses(IReadOnlyList<AgentHarnessDefinition> harnesses) => RenderHarnesses(settings: null, harnesses: harnesses);

    public static string RenderHarnesses(BotSettings? settings = null, IReadOnlyList<AgentHarnessDefinition>? harnesses = null)
    {
        IReadOnlyList<AgentHarnessDefinition> selectedHarnesses = harnesses ?? GetDefaultHarnesses();
        var builder = new StringBuilder();
        builder.AppendLine("P2 Agent Harness Plan");
        builder.AppendLine();
        builder.AppendLine("These are planning harnesses only. They do not execute image, OCR, audio, transcription, TTS, or generation work yet.");

        if (settings is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Harness model routes:");
            builder.AppendLine($"- image_agent route: {settings.OllamaImageModel}");
            builder.AppendLine($"- voice_agent route: {settings.OllamaVoiceModel}");
            builder.AppendLine("- image readiness target: pull/configure an Ollama vision model before enabling /describeimage vision execution.");
            builder.AppendLine("- voice readiness target: transcription still needs a dedicated audio/Whisper provider later; the voice route is for transcript summarization/task extraction.");
        }

        foreach (AgentHarnessDefinition harness in selectedHarnesses)
        {
            builder.AppendLine();
            builder.AppendLine($"## {harness.Name} ({harness.Status})");
            builder.AppendLine(harness.Purpose);
            AppendList(builder, "Tools", harness.Tools);
            AppendList(builder, "Safety rules", harness.SafetyRules);
            AppendList(builder, "Next implementation steps", harness.NextImplementationSteps);
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
