using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class StatusCommand : IBotCommand
{
    private readonly BotSettings _settings;

    public StatusCommand(BotSettings settings)
    {
        _settings = settings;
    }

    public string Name => "/status";
    public string Description => "Show bot runtime status.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!message.Text?.StartsWith("/status", StringComparison.OrdinalIgnoreCase) ?? true)
        {
            return new CommandResult(false, null);
        }

        bool dbReady = await dbContext.Database.CanConnectAsync(cancellationToken);

        string reply = $"""
Status

Database: {(dbReady ? "OK" : "Unavailable")}
Ollama URL: {_settings.OllamaUrl}
Ollama model: {_settings.OllamaModel}
Allowlist: {(_settings.AllowedChatIds.Count == 0 ? "disabled" : $"enabled ({_settings.AllowedChatIds.Count} chat IDs)")}
Message content logging: {(_settings.LogMessageContent ? "enabled" : "disabled")}
Apply migrations: {_settings.ApplyMigrations}
""";

        return new CommandResult(true, reply);
    }
}
