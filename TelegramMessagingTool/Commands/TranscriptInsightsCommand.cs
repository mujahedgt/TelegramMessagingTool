using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class TranscriptInsightsCommand : IBotCommand
{
    private readonly DocumentStorageService _documentStorage;
    private readonly TranscriptInsightsService _transcriptInsightsService;

    public TranscriptInsightsCommand(
        DocumentStorageService documentStorage,
        TranscriptInsightsService transcriptInsightsService)
    {
        _documentStorage = documentStorage;
        _transcriptInsightsService = transcriptInsightsService;
    }

    public string Name => "/transcriptinsights";

    public string Description => "Summarize and extract tasks from a saved transcript document.";

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
            return new CommandResult(true, "Usage: /transcriptinsights <transcript file id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == fileId && x.ConnectedUserId == user.Id, cancellationToken);
        if (file is null)
        {
            return new CommandResult(true, $"Transcript file #{fileId} was not found.");
        }

        if (!IsTranscriptFile(file))
        {
            return new CommandResult(true, $"File #{file.Id} is not a saved transcript document. Use /transcribe <audio-id> first, then run /transcriptinsights <transcript-file-id>.");
        }

        string transcriptText = await _documentStorage.ExtractTextAsync(file, cancellationToken, maxCharacters: 200_000);
        if (IsUnavailableTranscriptText(transcriptText))
        {
            return new CommandResult(true, transcriptText);
        }

        string insights = await _transcriptInsightsService.GenerateAsync(
            transcriptText,
            $"File #{file.Id} {file.OriginalFileName}",
            cancellationToken);

        return new CommandResult(true, $"Transcript insights for #{file.Id} {file.OriginalFileName}:\n\n{insights}");
    }

    private static bool IsTranscriptFile(UploadedFile file)
    {
        return string.Equals(file.Source, "transcript", StringComparison.OrdinalIgnoreCase)
            || file.OriginalFileName.EndsWith("-transcript.txt", StringComparison.OrdinalIgnoreCase)
            || file.OriginalFileName.Contains("transcript", StringComparison.OrdinalIgnoreCase)
                && file.OriginalFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnavailableTranscriptText(string text)
    {
        return text.StartsWith("File is missing on disk.", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("File is outside the current document sandbox.", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Unsupported file type for text extraction.", StringComparison.OrdinalIgnoreCase);
    }
}
