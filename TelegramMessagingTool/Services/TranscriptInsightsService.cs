using System.Text;

namespace TelegramMessagingTool.Services;

public sealed class TranscriptInsightsService
{
    private const int MaxTranscriptCharacters = 24_000;
    private readonly IChatClient _chatClient;

    public TranscriptInsightsService(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> GenerateAsync(string transcriptText, string sourceLabel, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            return "Transcript is empty; there is nothing to summarize or extract.";
        }

        string prompt = BuildPrompt(transcriptText, sourceLabel);
        return await _chatClient.AskAsync([new OllamaMessageDto("user", prompt)], cancellationToken, ModelTaskKind.Voice);
    }

    public static string BuildPrompt(string transcriptText, string sourceLabel)
    {
        string trimmedTranscript = transcriptText.Trim();
        if (trimmedTranscript.Length > MaxTranscriptCharacters)
        {
            trimmedTranscript = trimmedTranscript[..MaxTranscriptCharacters] + "\n[Transcript truncated for analysis.]";
        }

        var builder = new StringBuilder();
        builder.AppendLine("Analyze this saved audio transcript for the user.");
        builder.AppendLine("Use ONLY the transcript text. Do not invent details.");
        builder.AppendLine("Return a concise, practical response with:");
        builder.AppendLine("- Voice summary");
        builder.AppendLine("- Decisions or important facts");
        builder.AppendLine("- Tasks/action items, with owner/date only if present");
        builder.AppendLine("- Open questions or missing information");
        builder.AppendLine();
        builder.AppendLine($"Source transcript: {sourceLabel}");
        builder.AppendLine("Transcript:");
        builder.AppendLine(trimmedTranscript);
        return builder.ToString().Trim();
    }
}
