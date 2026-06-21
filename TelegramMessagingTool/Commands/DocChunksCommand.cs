using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class DocChunksCommand : IBotCommand
{
    public string Name => "/docchunks";

    public string Description => "Show document index/chunk status for a saved file.";

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

        string idText = CommandParser.GetArguments(messageText, Name);
        if (!int.TryParse(idText, out int fileId))
        {
            return new CommandResult(true, "Usage: /docchunks <file id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == fileId && x.ConnectedUserId == user.Id, cancellationToken);
        if (file is null)
        {
            return new CommandResult(true, $"File #{fileId} was not found.");
        }

        int chunkCount = await dbContext.DocumentChunks
            .CountAsync(x => x.UploadedFileId == fileId && x.ConnectedUserId == user.Id, cancellationToken);
        int totalCharacters = await dbContext.DocumentChunks
            .Where(x => x.UploadedFileId == fileId && x.ConnectedUserId == user.Id)
            .SumAsync(x => (int?)x.CharacterCount, cancellationToken) ?? 0;
        DateTime? lastIndexed = await dbContext.DocumentChunks
            .Where(x => x.UploadedFileId == fileId && x.ConnectedUserId == user.Id)
            .MaxAsync(x => (DateTime?)x.CreatedAt, cancellationToken);

        string lastIndexedText = lastIndexed.HasValue
            ? lastIndexed.Value.ToString("yyyy-MM-dd HH:mm 'UTC'")
            : "not indexed";

        return new CommandResult(true, $"File #{file.Id}: {file.OriginalFileName}\nChunks: {chunkCount}\nTotal indexed characters: {totalCharacters}\nLast indexed: {lastIndexedText}");
    }
}
