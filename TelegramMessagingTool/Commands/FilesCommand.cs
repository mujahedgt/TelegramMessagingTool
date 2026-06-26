using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class FilesCommand : IBotCommand
{
    private readonly DocumentStorageService _documentStorage;

    public FilesCommand(DocumentStorageService documentStorage)
    {
        _documentStorage = documentStorage;
    }

    public string Name => "/files";

    public string Description => "List your uploaded and created files.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!CommandParser.Matches(message.Text, Name))
        {
            return new CommandResult(false, null);
        }

        List<UploadedFile> files = await dbContext.UploadedFiles
            .Where(x => x.ConnectedUserId == user.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        if (files.Count == 0)
        {
            return new CommandResult(true, "No files saved yet. Upload a .txt/.md/.json/.csv/.pdf/.docx/.xlsx document or .png/.jpg/.jpeg/.webp/.gif image as a Telegram document, or use /createfile <name> <content>.");
        }

        string reply = "Saved files:\n" + string.Join("\n", files.Select(x => $"#{x.Id}: {x.OriginalFileName} ({x.SizeBytes} bytes, {x.Source})"));
        return new CommandResult(true, reply);
    }
}
