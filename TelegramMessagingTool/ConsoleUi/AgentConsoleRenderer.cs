using System.Text;

namespace TelegramMessagingTool.ConsoleUi;

public sealed record AgentConsoleSnapshot(
    string BotUsername,
    string OllamaUrl,
    string OllamaModel,
    string DatabaseConnection,
    string AccessMode,
    bool MessageContentLoggingEnabled,
    bool OnlineSearchEnabled,
    bool ApplyMigrations,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> Tools);

public enum ConsoleEventLevel
{
    Info,
    Success,
    Warning,
    Error
}

public static class AgentConsoleRenderer
{
    public static string RenderStartupPanel(AgentConsoleSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("╔════════════════════════════════════════════════════════════╗");
        builder.AppendLine("║        TelegramMessagingTool Agent Console                ║");
        builder.AppendLine("╚════════════════════════════════════════════════════════════╝");
        builder.AppendLine();
        builder.AppendLine("Status");
        builder.AppendLine("------");
        builder.AppendLine($"Bot                 : @{snapshot.BotUsername}");
        builder.AppendLine($"Model               : {snapshot.OllamaModel}");
        builder.AppendLine($"Ollama endpoint     : {snapshot.OllamaUrl}");
        builder.AppendLine($"Database            : {SummarizeDatabaseConnection(snapshot.DatabaseConnection)}");
        builder.AppendLine($"Apply migrations    : {(snapshot.ApplyMigrations ? "yes" : "no")}");
        builder.AppendLine($"Access mode         : {snapshot.AccessMode}");
        builder.AppendLine($"Message content logs: {(snapshot.MessageContentLoggingEnabled ? "enabled" : "disabled")}");
        builder.AppendLine($"Online search      : {(snapshot.OnlineSearchEnabled ? "enabled" : "disabled")}");
        builder.AppendLine();
        builder.AppendLine("Commands");
        builder.AppendLine("--------");
        AppendColumns(builder, snapshot.Commands, 3);
        builder.AppendLine();
        builder.AppendLine("Agent tools");
        builder.AppendLine("-----------");
        AppendColumns(builder, snapshot.Tools, 2);
        builder.AppendLine();
        builder.AppendLine("Quick start");
        builder.AppendLine("-----------");
        builder.AppendLine("Type directly in this console or ask Telegram:");
        builder.AppendLine("  /help                         Show commands");
        builder.AppendLine("  /tools                        Show available tools");
        builder.AppendLine("  What time is it?              Uses datetime when needed");
        builder.AppendLine("  Calculate 25 * 19             Uses calculator when needed");
        if (snapshot.OnlineSearchEnabled)
        {
            builder.AppendLine("  Search online for .NET news   Uses online_search when needed");
        }
        else
        {
            builder.AppendLine("  Search online for .NET news   Online search disabled; set ENABLE_ONLINE_SEARCH=true");
        }
        builder.AppendLine("  /exit                         Stop the console app gracefully");
        builder.AppendLine();
        builder.AppendLine("Safety warnings");
        builder.AppendLine("---------------");
        bool hasWarning = false;
        if (snapshot.AccessMode.Equals("public override", StringComparison.OrdinalIgnoreCase))
        {
            hasWarning = true;
            builder.AppendLine("! ALLOW_PUBLIC_ACCESS is enabled. Anyone who finds the bot can use it.");
        }
        else if (snapshot.AccessMode.Equals("locked", StringComparison.OrdinalIgnoreCase))
        {
            hasWarning = true;
            builder.AppendLine("! Telegram access is locked. Set ADMIN_CHAT_ID, ALLOWED_CHAT_IDS, or ALLOW_PUBLIC_ACCESS=true intentionally.");
        }

        if (snapshot.MessageContentLoggingEnabled)
        {
            hasWarning = true;
            builder.AppendLine("! LOG_MESSAGE_CONTENT is enabled. Logs may contain private chat text.");
        }

        if (!hasWarning)
        {
            builder.AppendLine("No immediate safety warnings.");
        }

        builder.AppendLine();
        builder.AppendLine("Runtime keys");
        builder.AppendLine("------------");
        builder.AppendLine("Type a message or command in the console to use the same local agent as Telegram.");
        builder.AppendLine("Use /exit or Ctrl+C to stop gracefully.");
        builder.AppendLine();
        builder.AppendLine("Live events");
        builder.AppendLine("-----------");
        builder.AppendLine("Incoming messages, commands, denied users, and errors will appear below.");
        return builder.ToString().TrimEnd();
    }

    public static string RenderEvent(string label, string actor, string detail, ConsoleEventLevel level)
    {
        string marker = level switch
        {
            ConsoleEventLevel.Success => "OK",
            ConsoleEventLevel.Warning => "WARN",
            ConsoleEventLevel.Error => "ERR",
            _ => "INFO"
        };

        string timestamp = DateTimeOffset.Now.ToString("HH:mm:ss");
        return $"[{timestamp}] [{marker}] {label,-10} {actor,-18} {detail}";
    }

    public static string SummarizeDatabaseConnection(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "not configured";
        }

        string[] parts = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var safeParts = new List<string>();
        foreach (string part in parts)
        {
            int equalsIndex = part.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            string key = part[..equalsIndex].Trim();
            string value = part[(equalsIndex + 1)..].Trim();
            if (IsSensitiveConnectionKey(key))
            {
                continue;
            }

            if (key.Equals("Server", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Database", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
            {
                safeParts.Add($"{key}={value}");
            }
        }

        return safeParts.Count == 0
            ? "configured (details hidden)"
            : string.Join("; ", safeParts);
    }

    private static bool IsSensitiveConnectionKey(string key)
    {
        return key.Equals("Password", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Pwd", StringComparison.OrdinalIgnoreCase)
            || key.Equals("User Id", StringComparison.OrdinalIgnoreCase)
            || key.Equals("User ID", StringComparison.OrdinalIgnoreCase)
            || key.Equals("UID", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Access Token", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendColumns(StringBuilder builder, IReadOnlyList<string> values, int columns)
    {
        if (values.Count == 0)
        {
            builder.AppendLine("  none");
            return;
        }

        int width = Math.Max(18, values.Max(x => x.Length) + 4);
        for (int i = 0; i < values.Count; i += columns)
        {
            var line = new StringBuilder("  ");
            for (int j = i; j < Math.Min(i + columns, values.Count); j++)
            {
                line.Append(values[j].PadRight(width));
            }

            builder.AppendLine(line.ToString().TrimEnd());
        }
    }
}
