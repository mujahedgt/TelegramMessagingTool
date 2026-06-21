using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class SystemInfoCommand : IBotCommand
{
    public string Name => "/systeminfo";

    public string Description => "Show read-only local operating system and runtime info.";

    public Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/systeminfo", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new CommandResult(false, null));
        }

        return Task.FromResult(new CommandResult(true, LocalDeviceInfoService.RenderSystemInfo()));
    }
}
