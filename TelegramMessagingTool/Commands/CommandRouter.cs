using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class CommandRouter
{
    private readonly IReadOnlyList<IBotCommand> _commands;

    public CommandRouter(IReadOnlyList<IBotCommand> commands)
    {
        _commands = commands;
    }

    public IReadOnlyList<IBotCommand> Commands => _commands;

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        foreach (IBotCommand command in _commands)
        {
            CommandResult result = await command.TryHandleAsync(message, user, dbContext, cancellationToken);
            if (result.Handled)
            {
                return result;
            }
        }

        return new CommandResult(false, null);
    }
}
