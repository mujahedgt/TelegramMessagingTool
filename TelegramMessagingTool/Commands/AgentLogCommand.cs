using System.Text;
using Telegram.Bot.Types;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class AgentLogCommand : IBotCommand
{
    private readonly BotSettings _settings;
    private readonly RuntimeEventBuffer _runtimeEventBuffer;

    public AgentLogCommand(BotSettings settings, RuntimeEventBuffer runtimeEventBuffer)
    {
        _settings = settings;
        _runtimeEventBuffer = runtimeEventBuffer;
    }

    public string Name => "/agentlog";

    public string Description => "Admin-only: show recent sanitized agent/tool/provider activity.";

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

        int count = ParseCount(CommandParser.GetArguments(message.Text ?? string.Empty, Name));
        IReadOnlyList<RuntimeEventEntry> entries = _runtimeEventBuffer.RecentEvents(count)
            .Where(IsAgentRelevant)
            .Take(count)
            .ToList();

        if (entries.Count == 0)
        {
            return Task.FromResult(new CommandResult(true, "Agent activity log\n\nNo recent agent/tool/provider activity was recorded."));
        }

        var builder = new StringBuilder();
        builder.AppendLine("Agent activity log");
        builder.AppendLine($"Recent entries: {entries.Count}");
        builder.AppendLine();
        foreach (RuntimeEventEntry entry in entries)
        {
            builder.AppendLine($"- {entry.TimestampUtc:HH:mm:ss}Z [{RenderLevel(entry.Level)}] {entry.Category}: {entry.Detail}");
        }

        return Task.FromResult(new CommandResult(true, builder.ToString().TrimEnd()));
    }

    private static int ParseCount(string raw)
    {
        return int.TryParse(raw, out int count) ? Math.Clamp(count, 1, 50) : 20;
    }

    private static bool IsAgentRelevant(RuntimeEventEntry entry)
    {
        string category = entry.Category;
        string detail = entry.Detail;
        return category.Contains("TOOL", StringComparison.OrdinalIgnoreCase)
            || category.Contains("AGENT", StringComparison.OrdinalIgnoreCase)
            || category.Contains("PROVIDER", StringComparison.OrdinalIgnoreCase)
            || category.Contains("PLUGIN", StringComparison.OrdinalIgnoreCase)
            || category.Contains("PENDING", StringComparison.OrdinalIgnoreCase)
            || category.Contains("APPROVAL", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("TOOL_", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("PENDING_", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("APPROVAL_", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("PLUGIN", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("PROVIDER", StringComparison.OrdinalIgnoreCase);
    }

    private static string RenderLevel(ConsoleEventLevel level) => level switch
    {
        ConsoleEventLevel.Error => "error",
        ConsoleEventLevel.Warning => "warn",
        ConsoleEventLevel.Success => "ok",
        _ => "info"
    };
}
