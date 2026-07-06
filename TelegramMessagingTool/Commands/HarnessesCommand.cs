using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class HarnessesCommand : IBotCommand
{
    private readonly BotSettings _settings;

    public HarnessesCommand(BotSettings settings)
    {
        _settings = settings;
    }

    public string Name => "/harnesses";

    public string Description => "Show image and voice agent harness readiness.";

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

        return Task.FromResult(new CommandResult(true, AgentHarnessCatalog.RenderHarnesses(_settings)));
    }
}
