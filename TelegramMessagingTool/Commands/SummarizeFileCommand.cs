using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class SummarizeFileCommand : IBotCommand
{
    private readonly DocumentIndexingService _indexingService;
    private readonly DocumentRetrievalService _retrievalService;
    private readonly DocumentSummaryService _summaryService;

    public SummarizeFileCommand(
        DocumentIndexingService indexingService,
        DocumentRetrievalService retrievalService,
        DocumentSummaryService summaryService)
    {
        _indexingService = indexingService;
        _retrievalService = retrievalService;
        _summaryService = summaryService;
    }

    public string Name => "/summarizefile";

    public string Description => "Summarize one saved/indexed document.";

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

        string args = CommandParser.GetArguments(messageText, Name);
        if (!int.TryParse(args, out int fileId))
        {
            return new CommandResult(true, "Usage: /summarizefile <file id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == fileId && x.ConnectedUserId == user.Id, cancellationToken);
        if (file is null)
        {
            return new CommandResult(true, $"File #{fileId} was not found.");
        }

        bool hasChunks = await dbContext.DocumentChunks
            .AnyAsync(x => x.UploadedFileId == fileId && x.ConnectedUserId == user.Id, cancellationToken);
        if (!hasChunks)
        {
            int indexedChunks = await _indexingService.IndexFileAsync(dbContext, user, fileId, cancellationToken);
            if (indexedChunks == 0)
            {
                return new CommandResult(true, $"File #{fileId} has no extractable text to summarize. If it is a scanned PDF, OCR is not supported yet.");
            }
        }

        IReadOnlyList<DocumentChunk> chunks = await _retrievalService.GetSummaryChunksAsync(
            dbContext,
            user,
            fileId,
            limit: 8,
            cancellationToken);

        string summary = await _summaryService.SummarizeAsync(chunks, cancellationToken);
        return new CommandResult(true, summary);
    }
}
