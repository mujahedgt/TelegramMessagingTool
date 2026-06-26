using System.Globalization;
using System.Text.RegularExpressions;

namespace TelegramMessagingTool.Services;

public static partial class ScheduleParser
{
    private static readonly string[] AbsoluteFormats = ["yyyy-MM-dd HH:mm"];

    public static bool TryParse(string? input, DateTime nowUtc, out ScheduleParseResult result)
    {
        result = ScheduleParseResult.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        nowUtc = EnsureUtc(nowUtc);
        string text = string.Join(' ', input.Trim().Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));

        if (TryParseAbsolute(text, nowUtc, out result))
        {
            return true;
        }

        if (TryParseTomorrow(text, nowUtc, out result))
        {
            return true;
        }

        return TryParseRelative(text, nowUtc, out result);
    }

    private static bool TryParseAbsolute(string text, DateTime nowUtc, out ScheduleParseResult result)
    {
        result = ScheduleParseResult.Empty;
        if (!DateTime.TryParseExact(
                text,
                AbsoluteFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed))
        {
            return false;
        }

        DateTime scheduledAtUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        if (scheduledAtUtc <= nowUtc)
        {
            return false;
        }

        result = BuildResult(scheduledAtUtc);
        return true;
    }

    private static bool TryParseTomorrow(string text, DateTime nowUtc, out ScheduleParseResult result)
    {
        result = ScheduleParseResult.Empty;
        Match match = TomorrowRegex().Match(text);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["hour"].Value, out int hour)
            || !int.TryParse(match.Groups["minute"].Value, out int minute)
            || hour is < 0 or > 23
            || minute is < 0 or > 59)
        {
            return false;
        }

        DateTime scheduledAtUtc = nowUtc.Date.AddDays(1).AddHours(hour).AddMinutes(minute);
        result = BuildResult(scheduledAtUtc);
        return true;
    }

    private static bool TryParseRelative(string text, DateTime nowUtc, out ScheduleParseResult result)
    {
        result = ScheduleParseResult.Empty;
        Match match = RelativeRegex().Match(text);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["amount"].Value, out int amount) || amount <= 0)
        {
            return false;
        }

        string unit = match.Groups["unit"].Value.ToLowerInvariant();
        DateTime scheduledAtUtc = unit switch
        {
            "m" => nowUtc.AddMinutes(amount),
            "h" => nowUtc.AddHours(amount),
            _ => DateTime.MinValue
        };

        if (scheduledAtUtc <= nowUtc)
        {
            return false;
        }

        result = BuildResult(scheduledAtUtc);
        return true;
    }

    private static ScheduleParseResult BuildResult(DateTime scheduledAtUtc)
    {
        scheduledAtUtc = EnsureUtc(scheduledAtUtc);
        return new ScheduleParseResult(scheduledAtUtc, scheduledAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    [GeneratedRegex("^tomorrow\\s+(?<hour>\\d{1,2}):(?<minute>\\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TomorrowRegex();

    [GeneratedRegex("^in\\s+(?<amount>\\d+)\\s*(?<unit>[mh])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RelativeRegex();
}

public sealed record ScheduleParseResult(DateTime ScheduledAtUtc, string DisplayText)
{
    public static ScheduleParseResult Empty { get; } = new(DateTime.MinValue, string.Empty);
}
