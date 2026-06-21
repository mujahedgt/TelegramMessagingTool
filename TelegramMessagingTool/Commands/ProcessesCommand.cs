using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ProcessesCommand : IBotCommand
{
    public string Name => "/processes";

    public string Description => "Show read-only local running processes by memory usage.";

    public Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return Task.FromResult(new CommandResult(false, null));
        }

        int limit = 10;
        string args = CommandParser.GetArguments(messageText, Name);
        if (!string.IsNullOrWhiteSpace(args) && int.TryParse(args, out int parsedLimit))
        {
            limit = parsedLimit;
        }

        return Task.FromResult(new CommandResult(true, LocalDeviceInfoService.RenderTopProcesses(limit)));
    }
}
