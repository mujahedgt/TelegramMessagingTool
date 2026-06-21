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
        if (!CommandParser.Matches(message.Text, Name))
        {
            return new CommandResult(false, null);
        }

        int deleted = await dbContext.Messages
            .Where(x => x.ConnectedUserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        return new CommandResult(true, $"Conversation reset. Deleted {deleted} stored messages.");
    }
}
