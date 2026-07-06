using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class RiskConfigCommand : IBotCommand
{
    private readonly BotSettings _settings;

    public RiskConfigCommand(BotSettings settings)
    {
        _settings = settings;
    }

    public string Name => "/riskconfig";

    public string Description => "Admin-only: show risky runtime feature flags without secrets.";

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

        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return Task.FromResult(new CommandResult(true, BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId)));
        }

        return Task.FromResult(new CommandResult(true, RenderSummary(_settings)));
    }

    public static string RenderSummary(BotSettings settings)
    {
        return RuntimeRiskSummary.RenderRiskConfig(settings);
    }
}
