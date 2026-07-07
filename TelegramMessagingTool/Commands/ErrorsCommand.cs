using System.Text;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ErrorsCommand : IBotCommand
{
    private readonly BotSettings _settings;
    private readonly RuntimeEventBuffer _runtimeEventBuffer;

    public ErrorsCommand(BotSettings settings, RuntimeEventBuffer runtimeEventBuffer)
    {
        _settings = settings;
        _runtimeEventBuffer = runtimeEventBuffer;
    }

    public string Name => "/errors";

    public string Description => "Admin-only: show recent metadata-only runtime warnings/errors.";

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

        string arguments = CommandParser.GetArguments(message.Text, Name);
        int requestedCount = ParseCount(arguments);
        int clampedCount = Math.Clamp(requestedCount, 1, 50);
        IReadOnlyList<RuntimeEventEntry> entries = _runtimeEventBuffer.RecentWarningsAndErrors(clampedCount);

        var builder = new StringBuilder();
        builder.AppendLine("Recent runtime warnings/errors");
        builder.AppendLine($"showing {entries.Count}, limit {clampedCount}");
        builder.AppendLine();

        if (entries.Count == 0)
        {
            builder.AppendLine("No recent warnings/errors are buffered.");
            builder.AppendLine("Only metadata is stored; raw message content and secrets are not shown.");
            return Task.FromResult(new CommandResult(true, builder.ToString().TrimEnd()));
        }

        foreach (RuntimeEventEntry entry in entries)
        {
            builder.AppendLine($"- {entry.TimestampUtc:yyyy-MM-dd HH:mm:ss}Z [{entry.Level}] {entry.Category}: {entry.Detail}");
        }

        builder.AppendLine();
        builder.AppendLine("Only metadata is stored; raw message content and secrets are not shown.");
        return Task.FromResult(new CommandResult(true, builder.ToString().TrimEnd()));
    }

    private static int ParseCount(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return 10;
        }

        string firstToken = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        return int.TryParse(firstToken, out int parsed)
            ? parsed
            : 10;
    }
}
