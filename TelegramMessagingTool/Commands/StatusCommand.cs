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
        if (!CommandParser.Matches(message.Text, Name))
        {
            return new CommandResult(false, null);
        }

        bool dbReady = await dbContext.Database.CanConnectAsync(cancellationToken);

        string reply = $"""
Status

Database: {(dbReady ? "OK" : "Unavailable")}
Ollama URL: {_settings.OllamaUrl}
Ollama model: {_settings.OllamaModel}
Access mode: {BotAccessPolicy.DescribeAccessMode(_settings.AllowedChatIds, _settings.AdminChatId, _settings.AllowPublicAccess)}
Document embeddings: {(_settings.EnableDocumentEmbeddings ? "enabled" : "disabled")}
Online search: {(_settings.EnableOnlineSearch ? "enabled" : "disabled")}
Message content logging: {(_settings.LogMessageContent ? "enabled" : "disabled")}
Apply migrations: {_settings.ApplyMigrations}
""";

        return new CommandResult(true, reply);
    }
}
