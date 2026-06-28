using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class DescribeImageCommand : IBotCommand
{
    private readonly BotSettings _settings;

    public DescribeImageCommand(BotSettings settings)
    {
        _settings = settings;
    }

    public string Name => "/describeimage";

    public string Description => "Show metadata for a saved image before vision description is implemented.";

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
        if (!int.TryParse(idText, out int imageFileId))
        {
            return new CommandResult(true, "Usage: /describeimage <image-file-id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == imageFileId && x.ConnectedUserId == user.Id, cancellationToken);

        if (file is null)
        {
            return new CommandResult(true, $"Image file #{imageFileId} was not found.");
        }

        if (!DocumentStorageService.IsImageFileName(file.OriginalFileName)
            && !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(true, $"File #{file.Id} is not an image. Use /files to list saved files or /images to list saved image files.");
        }

        string diskStatus = File.Exists(file.AbsolutePath) ? "present" : "missing";
        string reply = $"""
Image #{file.Id}: {file.OriginalFileName}

Metadata:
- Content type: {file.ContentType}
- Size: {file.SizeBytes} bytes
- Source: {file.Source}
- Disk: {diskStatus}
- Image model route: {_settings.OllamaImageModel}

Image description/OCR is not implemented yet. This command is a safe metadata-only image-agent harness step.
""";

        return new CommandResult(true, reply);
    }
}
