using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class SummarizeDocsCommand : IBotCommand
{
    private readonly DocumentRetrievalService _retrievalService;
    private readonly DocumentSummaryService _summaryService;

    public SummarizeDocsCommand(
        DocumentRetrievalService retrievalService,
        DocumentSummaryService summaryService)
    {
        _retrievalService = retrievalService;
        _summaryService = summaryService;
    }

    public string Name => "/summarizedocs";

    public string Description => "Summarize all indexed documents.";

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

        bool hasChunks = await dbContext.DocumentChunks
            .AnyAsync(x => x.ConnectedUserId == user.Id, cancellationToken);
        if (!hasChunks)
        {
            return new CommandResult(true, "No indexed document text found yet. Upload or create files, then run /indexdocs or /indexfile <file id>.");
        }

        IReadOnlyList<DocumentChunk> chunks = await _retrievalService.GetSummaryChunksAsync(
            dbContext,
            user,
            fileId: null,
            limit: 12,
            cancellationToken);

        string summary = await _summaryService.SummarizeAsync(chunks, cancellationToken);
        return new CommandResult(true, summary);
    }
}
