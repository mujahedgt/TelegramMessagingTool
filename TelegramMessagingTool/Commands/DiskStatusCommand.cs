using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class DiskStatusCommand : IBotCommand
{
    public string Name => "/diskstatus";

    public string Description => "Show read-only local disk space status.";

    public Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/diskstatus", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new CommandResult(false, null));
        }

        return Task.FromResult(new CommandResult(true, LocalDeviceInfoService.RenderDiskStatus()));
    }
}
