using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ReadFileCommand : IBotCommand
{
    private readonly DocumentStorageService _documentStorage;

    public ReadFileCommand(DocumentStorageService documentStorage)
    {
        _documentStorage = documentStorage;
    }

    public string Name => "/readfile";

    public string Description => "Read a saved file by ID.";

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
            return new CommandResult(true, "Usage: /readfile <file id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == fileId && x.ConnectedUserId == user.Id, cancellationToken);

        if (file is null)
        {
            return new CommandResult(true, $"File #{fileId} was not found.");
        }

        string text = await _documentStorage.ExtractTextAsync(file, cancellationToken);
        string reply = $"File #{file.Id}: {file.OriginalFileName}\n\n{text}";
        return new CommandResult(true, reply);
    }
}
