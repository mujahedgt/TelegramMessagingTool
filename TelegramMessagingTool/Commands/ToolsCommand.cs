using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.Commands;

public sealed class ToolsCommand : IBotCommand
{
    private readonly ToolRegistry _toolRegistry;

    public ToolsCommand(ToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public string Name => "/tools";

    public string Description => "List available agent tools.";

    public Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!CommandParser.Matches(message.Text, Name))
        {
            return Task.FromResult(new CommandResult(false, null));
        }

        string reply = _toolRegistry.RenderToolList() + "\n\nTool calling is automatic: ask a normal question, and the model can request one safe tool when needed.";
        return Task.FromResult(new CommandResult(true, reply));
    }
}
