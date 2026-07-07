using System.Text.RegularExpressions;
using TelegramMessagingTool.ConsoleUi;

namespace TelegramMessagingTool.Services;

public sealed partial class RuntimeEventBuffer
{
    private readonly object _gate = new();
    private readonly Queue<RuntimeEventEntry> _entries = new();
    private readonly int _capacity;

    public RuntimeEventBuffer(int capacity = 200)
    {
        _capacity = Math.Clamp(capacity, 1, 1_000);
    }

    public void Record(ConsoleEventLevel level, string category, string detail)
    {
        var entry = new RuntimeEventEntry(
            TimestampUtc: DateTimeOffset.UtcNow,
            Level: level,
            Category: SanitizeToken(category),
            Detail: RedactAndTruncate(detail, 220));

        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    public IReadOnlyList<RuntimeEventEntry> RecentWarningsAndErrors(int count)
    {
        int limit = Math.Clamp(count, 1, 50);
        lock (_gate)
        {
            return _entries
                .Where(x => x.Level is ConsoleEventLevel.Warning or ConsoleEventLevel.Error)
                .Reverse()
                .Take(limit)
                .ToList();
        }
    }

    private static string SanitizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        string compact = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        compact = SafeTokenCharacters().Replace(compact, "_");
        return compact.Length <= 80 ? compact : compact[..80];
    }

    private static string RedactAndTruncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        string compact = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        compact = SecretAssignmentPattern().Replace(compact, "$1=[REDACTED]");
        compact = TelegramTokenPattern().Replace(compact, "[REDACTED_TOKEN]");
        compact = GitHubTokenPattern().Replace(compact, "[REDACTED_TOKEN]");
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }

    [GeneratedRegex("[^A-Za-z0-9_.:/=-]+")]
    private static partial Regex SafeTokenCharacters();

    [GeneratedRegex("(?i)\\b([A-Z0-9_]*(?:TOKEN|PASSWORD|SECRET|KEY|CONNECTION)[A-Z0-9_]*)\\s*=\\s*[^\\s;]+")]
    private static partial Regex SecretAssignmentPattern();

    [GeneratedRegex("\\b\\d{6,}:[A-Za-z0-9_-]{20,}\\b")]
    private static partial Regex TelegramTokenPattern();

    [GeneratedRegex("\\b(?:ghp|github_pat|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{10,}\\b")]
    private static partial Regex GitHubTokenPattern();
}

public sealed record RuntimeEventEntry(
    DateTimeOffset TimestampUtc,
    ConsoleEventLevel Level,
    string Category,
    string Detail);
