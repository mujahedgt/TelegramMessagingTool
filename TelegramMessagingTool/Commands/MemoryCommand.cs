using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class MemoryCommand : IBotCommand
{
    public string Name => "/memory";
    public string Description => "Show saved memories.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!message.Text?.StartsWith("/memory", StringComparison.OrdinalIgnoreCase) ?? true)
        {
            return new CommandResult(false, null);
        }

        List<Memory> memories = await dbContext.Memories
            .Where(x => x.ConnectedUserId == user.Id)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        if (memories.Count == 0)
        {
            return new CommandResult(true, "No saved memories yet. Use /remember <fact> to save one.");
        }

        string reply = "Saved memories:\n" + string.Join("\n", memories.Select(x => $"#{x.Id}: {x.Content}"));
        return new CommandResult(true, reply);
    }
}
