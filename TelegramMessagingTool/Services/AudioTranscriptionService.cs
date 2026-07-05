using System.Diagnostics;
using System.Text;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed record AudioTranscriptionResult(bool Success, string Output)
{
    public static AudioTranscriptionResult Ok(string output) => new(true, output);

    public static AudioTranscriptionResult Failed(string output) => new(false, output);
}

public interface IAudioTranscriptionService
{
    Task<AudioTranscriptionResult> TranscribeAsync(
        UploadedFile audioFile,
        CancellationToken cancellationToken);
}

public sealed class LocalCommandAudioTranscriptionService : IAudioTranscriptionService
{
    private readonly string _command;
    private readonly string _argumentsTemplate;
    private readonly TimeSpan _timeout;

    public LocalCommandAudioTranscriptionService(string command, string argumentsTemplate, TimeSpan timeout)
    {
        _command = command.Trim();
        _argumentsTemplate = string.IsNullOrWhiteSpace(argumentsTemplate) ? "{file}" : argumentsTemplate.Trim();
        _timeout = timeout;
    }

    public async Task<AudioTranscriptionResult> TranscribeAsync(UploadedFile audioFile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_command))
        {
            return AudioTranscriptionResult.Failed("No local audio transcription command is configured.");
        }

        string audioPath = Path.GetFullPath(audioFile.AbsolutePath);
        if (!File.Exists(audioPath))
        {
            return AudioTranscriptionResult.Failed("Audio file is missing on disk.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        string renderedArguments = _argumentsTemplate.Replace("{file}", audioPath, StringComparison.OrdinalIgnoreCase);
        foreach (string argument in SplitArguments(renderedArguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start local transcription command.");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            string output = (await outputTask).Trim();
            string error = (await errorTask).Trim();
            if (process.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(error) ? output : error;
                return AudioTranscriptionResult.Failed($"Local transcription provider exited with code {process.ExitCode}. {Truncate(detail)}".Trim());
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                return AudioTranscriptionResult.Failed("Local transcription provider returned an empty transcript.");
            }

            return AudioTranscriptionResult.Ok(output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AudioTranscriptionResult.Failed("Local transcription provider timed out.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return AudioTranscriptionResult.Failed($"Local transcription provider could not be started: {ex.Message}");
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
