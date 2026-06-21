using System.Text;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class DocumentSummaryService
{
    private readonly IChatClient _chatClient;

    public DocumentSummaryService(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> SummarizeAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return "No indexed document text found to summarize.";
        }

        string prompt = BuildPrompt(chunks);
        return await _chatClient.AskAsync([new OllamaMessageDto("user", prompt)], cancellationToken);
    }

    public static string BuildPrompt(IReadOnlyList<DocumentChunk> chunks)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Summarize the user's indexed document excerpts.");
        builder.AppendLine("Use ONLY the excerpts below. Do not invent facts.");
        builder.AppendLine("Return a practical summary with:");
        builder.AppendLine("- Main topic");
        builder.AppendLine("- Important details");
        builder.AppendLine("- Dates, numbers, names, or decisions if present");
        builder.AppendLine("- Open questions or missing information if relevant");
        builder.AppendLine("Include citations using file ID, filename, and chunk number.");
        builder.AppendLine();
        builder.AppendLine("Document excerpts:");

        foreach (DocumentChunk chunk in chunks)
        {
            builder.AppendLine($"[File #{chunk.UploadedFileId} {chunk.OriginalFileName}, chunk {chunk.ChunkNumber}]");
            builder.AppendLine(chunk.Text.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("Write the summary with citations.");
        return builder.ToString().Trim();
    }
}
