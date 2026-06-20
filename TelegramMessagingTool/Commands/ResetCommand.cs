using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class ResetCommand : IBotCommand
{
    public string Name => "/reset";
    public string Description => "Clear your conversation history.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!message.Text?.StartsWith("/reset", StringComparison.OrdinalIgnoreCase) ?? true)
        {
            return new CommandResult(false, null);
        }

        int deleted = await dbContext.Messages
            .Where(x => x.ConnectedUserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        return new CommandResult(true, $"Conversation reset. Deleted {deleted} stored messages.");
    }
}
