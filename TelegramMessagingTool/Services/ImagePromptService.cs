using System.Text;

namespace TelegramMessagingTool.Services;

public sealed class ImagePromptService
{
    private const int MaxContextCharacters = 12_000;
    private readonly IChatClient _chatClient;

    public ImagePromptService(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> GenerateFromIdeaAsync(string idea, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idea))
        {
            return "Idea is empty; provide a short image idea after /imageprompt.";
        }

        string prompt = BuildIdeaPrompt(idea);
        return await _chatClient.AskAsync([new OllamaMessageDto("user", prompt)], cancellationToken, ModelTaskKind.Image);
    }

    public async Task<string> GenerateFromImageContextAsync(
        string imageContext,
        string sourceLabel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageContext))
        {
            return "Image context is empty; there is not enough information to draft a prompt.";
        }

        string prompt = BuildImageContextPrompt(imageContext, sourceLabel);
        return await _chatClient.AskAsync([new OllamaMessageDto("user", prompt)], cancellationToken, ModelTaskKind.Image);
    }

    public static string BuildIdeaPrompt(string idea)
    {
        string trimmedIdea = TrimContext(idea);
        var builder = new StringBuilder();
        builder.AppendLine("Turn this user idea into a practical image-generation prompt.");
        builder.AppendLine("Do not generate an image. Return prompt text only.");
        builder.AppendLine("Avoid copyrighted logos, private data, unsafe content, and claims that visible text will be exact unless explicitly provided.");
        builder.AppendLine("Return exactly these sections:");
        builder.AppendLine("- Prompt");
        builder.AppendLine("- Negative prompt");
        builder.AppendLine("- Notes");
        builder.AppendLine();
        builder.AppendLine("User idea:");
        builder.AppendLine(trimmedIdea);
        return builder.ToString().Trim();
    }

    public static string BuildImageContextPrompt(string imageContext, string sourceLabel)
    {
        string trimmedContext = TrimContext(imageContext);
        var builder = new StringBuilder();
        builder.AppendLine("Create a practical image-generation prompt based on this saved image context.");
        builder.AppendLine("Use ONLY the provided image context. Do not invent details that are not described.");
        builder.AppendLine("Do not generate an image. Return prompt text only.");
        builder.AppendLine("Avoid copyrighted logos, private data, unsafe content, and exact-text claims unless the context explicitly includes readable text.");
        builder.AppendLine("Return exactly these sections:");
        builder.AppendLine("- Prompt");
        builder.AppendLine("- Negative prompt");
        builder.AppendLine("- Notes");
        builder.AppendLine();
        builder.AppendLine($"Source image: {sourceLabel}");
        builder.AppendLine("Image context:");
        builder.AppendLine(trimmedContext);
        return builder.ToString().Trim();
    }

    private static string TrimContext(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length > MaxContextCharacters
            ? trimmed[..MaxContextCharacters] + "\n[Context truncated for prompt drafting.]"
            : trimmed;
    }
}
