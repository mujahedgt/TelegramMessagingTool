using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class CreateFileCommand : IBotCommand
{
    private readonly DocumentStorageService _documentStorage;

    public CreateFileCommand(DocumentStorageService documentStorage)
    {
        _documentStorage = documentStorage;
    }

    public string Name => "/createfile";

    public string Description => "Create a sandboxed txt/md/json/csv/pdf/docx/xlsx file.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/createfile", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        string args = messageText["/createfile".Length..].Trim();
        int separatorIndex = args.IndexOf(' ');
        if (separatorIndex <= 0 || separatorIndex == args.Length - 1)
        {
            return new CommandResult(true, "Usage: /createfile <filename.txt|md|json|csv|pdf|docx|xlsx> <content>");
        }

        string fileName = args[..separatorIndex].Trim();
        string content = args[(separatorIndex + 1)..].Trim();

        try
        {
            UploadedFile file = await _documentStorage.CreateFileAsync(user, fileName, content, cancellationToken);
            dbContext.UploadedFiles.Add(file);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new CommandResult(true, $"File created as #{file.Id}: {file.OriginalFileName}\nUse /readfile {file.Id} to read it or /files to list saved files.");
        }
        catch (InvalidOperationException ex)
        {
            return new CommandResult(true, ex.Message);
        }
    }
}
