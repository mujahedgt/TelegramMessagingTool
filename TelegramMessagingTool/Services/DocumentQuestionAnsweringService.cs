using System.Text;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class DocumentQuestionAnsweringService
{
    private readonly IChatClient _chatClient;

    public DocumentQuestionAnsweringService(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> AnswerAsync(
        string question,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return "I could not find that in the indexed document text.";
        }

        string prompt = BuildPrompt(question, chunks);
        return await _chatClient.AskAsync([new OllamaMessageDto("user", prompt)], cancellationToken);
    }

    public static string BuildPrompt(string question, IReadOnlyList<DocumentChunk> chunks)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are answering a question about the user's saved documents.");
        builder.AppendLine("Use ONLY the document excerpts below.");
        builder.AppendLine("If the answer is not present, say exactly: I could not find that in the indexed document text.");
        builder.AppendLine("Do not invent facts. Include citations using file ID, filename, and chunk number.");
        builder.AppendLine();
        builder.AppendLine("Question:");
        builder.AppendLine(question.Trim());
        builder.AppendLine();
        builder.AppendLine("Document excerpts:");

        foreach (DocumentChunk chunk in chunks)
        {
            builder.AppendLine($"[File #{chunk.UploadedFileId} {chunk.OriginalFileName}, chunk {chunk.ChunkNumber}]");
            builder.AppendLine(chunk.Text.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("Answer with citations.");
        return builder.ToString().Trim();
    }
}
