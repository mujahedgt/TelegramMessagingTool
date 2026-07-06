using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ImagesCommand : IBotCommand
{
    public string Name => "/images";

    public string Description => "List saved sandboxed image files.";

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
            return new CommandResult(true, "No images saved yet. Upload a .png/.jpg/.jpeg/.webp/.gif image as a Telegram document, then use /images again. Use /describeimage <id> for metadata or gated local vision description.");
        }

        string reply = "Saved images:\n"
            + string.Join("\n", images.Select(x => $"#{x.Id}: {x.OriginalFileName} ({x.SizeBytes} bytes, {x.Source})"))
            + "\n\nUse /describeimage <id> for metadata or gated local vision description.";

        return new CommandResult(true, reply);
    }
}
