using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class ForgetCommand : IBotCommand
{
    public string Name => "/forget";
    public string Description => "Delete a saved memory by ID.";

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
        if (!int.TryParse(idText, out int memoryId))
        {
            return new CommandResult(true, "Usage: /forget <memory id>");
        }

        int deleted = await dbContext.Memories
            .Where(x => x.Id == memoryId && x.ConnectedUserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0
            ? new CommandResult(true, $"Memory #{memoryId} was not found.")
            : new CommandResult(true, $"Memory #{memoryId} deleted.");
    }
}
