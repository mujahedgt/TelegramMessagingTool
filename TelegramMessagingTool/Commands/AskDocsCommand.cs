using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class AskDocsCommand : IBotCommand
{
    private readonly DocumentRetrievalService _retrievalService;
    private readonly DocumentQuestionAnsweringService _questionAnsweringService;

    public AskDocsCommand(
        DocumentRetrievalService retrievalService,
        DocumentQuestionAnsweringService questionAnsweringService)
    {
        _retrievalService = retrievalService;
        _questionAnsweringService = questionAnsweringService;
    }

    public string Name => "/askdocs";

    public string Description => "Ask a question across all indexed documents.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/askdocs", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        string question = messageText["/askdocs".Length..].Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            return new CommandResult(true, "Usage: /askdocs <question>");
        }

        bool hasChunks = await dbContext.DocumentChunks
            .AnyAsync(x => x.ConnectedUserId == user.Id, cancellationToken);
        if (!hasChunks)
        {
            return new CommandResult(true, "No indexed document text found yet. Upload or create files, then run /indexdocs or /indexfile <file id>.");
        }

        IReadOnlyList<DocumentChunk> chunks = await _retrievalService.SearchAsync(
            dbContext,
            user,
            question,
            fileId: null,
            limit: 5,
            cancellationToken);

        string answer = await _questionAnsweringService.AnswerAsync(question, chunks, cancellationToken);
        return new CommandResult(true, answer);
    }
}
