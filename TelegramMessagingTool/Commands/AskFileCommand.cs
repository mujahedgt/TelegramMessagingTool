using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class AskFileCommand : IBotCommand
{
    private readonly DocumentIndexingService _indexingService;
    private readonly DocumentRetrievalService _retrievalService;
    private readonly DocumentQuestionAnsweringService _questionAnsweringService;

    public AskFileCommand(
        DocumentIndexingService indexingService,
        DocumentRetrievalService retrievalService,
        DocumentQuestionAnsweringService questionAnsweringService)
    {
        _indexingService = indexingService;
        _retrievalService = retrievalService;
        _questionAnsweringService = questionAnsweringService;
    }

    public string Name => "/askfile";

    public string Description => "Ask a question about one saved/indexed document.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/askfile", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        string args = messageText["/askfile".Length..].Trim();
        string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out int fileId))
        {
            return new CommandResult(true, "Usage: /askfile <file id> <question>");
        }

        string question = parts[1].Trim();
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
                return new CommandResult(true, $"File #{fileId} has no extractable text to ask about. If it is a scanned PDF, OCR is not supported yet.");
            }
        }

        IReadOnlyList<DocumentChunk> chunks = await _retrievalService.SearchAsync(
            dbContext,
            user,
            question,
            fileId,
            limit: 4,
            cancellationToken);

        string answer = await _questionAnsweringService.AnswerAsync(question, chunks, cancellationToken);
        return new CommandResult(true, answer);
    }
}
