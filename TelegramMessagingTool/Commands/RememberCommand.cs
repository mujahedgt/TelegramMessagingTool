using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class RememberCommand : IBotCommand
{
    public string Name => "/remember";
    public string Description => "Save a durable memory.";

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

        string content = CommandParser.GetArguments(messageText, Name);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new CommandResult(true, "Usage: /remember <fact to save>");
        }

        if (content.Length > 1000)
        {
            content = content[..1000];
        }

        var memory = new Memory
        {
            ConnectedUserId = user.Id,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Memories.Add(memory);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CommandResult(true, $"Memory saved as #{memory.Id}.");
    }
}
