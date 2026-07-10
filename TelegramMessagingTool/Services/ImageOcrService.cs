using System.Diagnostics;
using System.Text;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed record ImageOcrResult(bool Success, string Output)
{
    public static ImageOcrResult Ok(string output) => new(true, output);

    public static ImageOcrResult Failed(string output) => new(false, output);
}

public interface IImageOcrService
{
    Task<ImageOcrResult> ExtractTextAsync(
        UploadedFile imageFile,
        CancellationToken cancellationToken);
}

public sealed class LocalCommandImageOcrService : IImageOcrService
{
    private const int MaxProviderOutputCharacters = 100_000;

    private readonly string _command;
    private readonly string _argumentsTemplate;
    private readonly TimeSpan _timeout;

    public LocalCommandImageOcrService(string command, string argumentsTemplate, TimeSpan timeout)
    {
        _command = command.Trim();
        _argumentsTemplate = string.IsNullOrWhiteSpace(argumentsTemplate) ? "{file}" : argumentsTemplate.Trim();
        _timeout = timeout;
    }

    public async Task<ImageOcrResult> ExtractTextAsync(UploadedFile imageFile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_command))
        {
            return ImageOcrResult.Failed("No local image OCR command is configured.");
        }

        string imagePath = Path.GetFullPath(imageFile.AbsolutePath);
        if (!File.Exists(imagePath))
        {
            return ImageOcrResult.Failed("Image file is missing on disk.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in SplitArguments(_argumentsTemplate))
        {
            startInfo.ArgumentList.Add(argument.Replace("{file}", imagePath, StringComparison.OrdinalIgnoreCase));
        }

        try
        {
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start local image OCR command.");
            Task<string> outputTask = ReadLimitedAsync(process.StandardOutput, MaxProviderOutputCharacters, cancellationToken);
            Task<string> errorTask = ReadLimitedAsync(process.StandardError, MaxProviderOutputCharacters, cancellationToken);
            Task waitTask = process.WaitForExitAsync(cancellationToken);
            Task timeoutTask = Task.Delay(_timeout, cancellationToken);
            if (await Task.WhenAny(waitTask, timeoutTask) != waitTask)
            {
                TryKillProcessTree(process);
                await process.WaitForExitAsync(CancellationToken.None);
                return ImageOcrResult.Failed("Local image OCR provider timed out.");
            }

            await waitTask;

            string output = (await outputTask).Trim();
            string error = (await errorTask).Trim();
            if (process.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(error) ? output : error;
                return ImageOcrResult.Failed($"Local image OCR provider exited with code {process.ExitCode}. {Truncate(detail)}".Trim());
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                return ImageOcrResult.Failed("Local image OCR provider returned empty text.");
            }

            return ImageOcrResult.Ok(Truncate(output));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ImageOcrResult.Failed($"Local image OCR provider could not be started: {ex.Message}");
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int maxCharacters, CancellationToken cancellationToken)
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

    private static void TryKillProcessTree(Process process)
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

    internal static IReadOnlyList<string> SplitArguments(string arguments)
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

    private static string Truncate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 1000 ? value : value[..1000] + "...";
    }
}
