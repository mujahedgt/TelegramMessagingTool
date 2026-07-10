using System.Diagnostics;
using System.Text;

namespace TelegramMessagingTool.Services;

internal static class LocalCommandProcessSupport
{
    public const int MaxProviderOutputCharacters = 100_000;

    public static void AddTemplateArguments(
        ProcessStartInfo startInfo,
        string argumentsTemplate,
        IReadOnlyDictionary<string, string> replacements)
    {
        foreach (string argument in SplitArguments(argumentsTemplate))
        {
            string rendered = argument;
            foreach (KeyValuePair<string, string> replacement in replacements)
            {
                rendered = rendered.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase);
            }

            startInfo.ArgumentList.Add(rendered);
        }
    }

    public static IReadOnlyList<string> SplitArguments(string arguments)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < arguments.Length; i++)
        {
            char ch = arguments[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    public static async Task<LocalCommandProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start local provider command.");
        Task<string> outputTask = ReadLimitedAsync(process.StandardOutput, MaxProviderOutputCharacters, cancellationToken);
        Task<string> errorTask = ReadLimitedAsync(process.StandardError, MaxProviderOutputCharacters, cancellationToken);
        Task waitTask = process.WaitForExitAsync(cancellationToken);
        Task timeoutTask = Task.Delay(timeout, cancellationToken);

        if (await Task.WhenAny(waitTask, timeoutTask) != waitTask)
        {
            TryKillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            return new LocalCommandProcessResult(false, -1, string.Empty, string.Empty, TimedOut: true);
        }

        await waitTask;
        string output = (await outputTask).Trim();
        string error = (await errorTask).Trim();
        return new LocalCommandProcessResult(true, process.ExitCode, output, error, TimedOut: false);
    }

    public static async Task<string> ReadLimitedAsync(StreamReader reader, int maxCharacters, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(capacity: Math.Min(maxCharacters, 4096));
        char[] buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            int remaining = maxCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }
        }

        return builder.ToString();
    }

    public static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    public static string Truncate(string value, int maxCharacters = 1000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxCharacters ? value : value[..maxCharacters] + "...";
    }
}

internal sealed record LocalCommandProcessResult(
    bool Completed,
    int ExitCode,
    string Output,
    string Error,
    bool TimedOut);
