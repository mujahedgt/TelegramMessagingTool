using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ImagesCommand : IBotCommand
{
    public string Name => "/images";

    public string Description => "List saved sandboxed image files for the planned image-agent harness.";

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

        List<UploadedFile> images = await dbContext.UploadedFiles
            .Where(x => x.ConnectedUserId == user.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        images = images
            .Where(x => DocumentStorageService.IsImageFileName(x.OriginalFileName)
                || x.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();

        if (images.Count == 0)
        {
            return new CommandResult(true, "No images saved yet. Upload a .png/.jpg/.jpeg/.webp/.gif image as a Telegram document, then use /images again. Image description/OCR is planned next but not implemented yet.");
        }

        string reply = "Saved images for image_agent harness:\n"
            + string.Join("\n", images.Select(x => $"#{x.Id}: {x.OriginalFileName} ({x.SizeBytes} bytes, {x.Source})"))
            + "\n\nNext planned commands: /describeimage <id> and OCR/image prompt helpers.";

        return new CommandResult(true, reply);
    }
}
