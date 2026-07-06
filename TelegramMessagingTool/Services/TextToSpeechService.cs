using System.Diagnostics;
using System.Text;

namespace TelegramMessagingTool.Services;

public sealed record TextToSpeechResult(bool Success, byte[] AudioBytes, string OutputExtension, string Message)
{
    public static TextToSpeechResult Ok(byte[] audioBytes, string outputExtension, string message) => new(true, audioBytes, NormalizeExtension(outputExtension), message);

    public static TextToSpeechResult Failed(string message) => new(false, Array.Empty<byte>(), ".mp3", message);

    private static string NormalizeExtension(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ".mp3";
        }

        string normalized = value.Trim().TrimStart('.').ToLowerInvariant();
        return "." + normalized;
    }
}

public interface ITextToSpeechService
{
    Task<TextToSpeechResult> SynthesizeAsync(string text, CancellationToken cancellationToken);
}

public sealed class LocalCommandTextToSpeechService : ITextToSpeechService
{
    private readonly string _command;
    private readonly string _argumentsTemplate;
    private readonly TimeSpan _timeout;
    private readonly string _outputExtension;

    public LocalCommandTextToSpeechService(
        string command,
        string argumentsTemplate,
        TimeSpan timeout,
        string outputExtension)
    {
        _command = command.Trim();
        _argumentsTemplate = string.IsNullOrWhiteSpace(argumentsTemplate) ? "{text} {output}" : argumentsTemplate.Trim();
        _timeout = timeout;
        _outputExtension = BotConfiguration.NormalizeTextToSpeechOutputExtension(outputExtension);
    }

    public async Task<TextToSpeechResult> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_command))
        {
            return TextToSpeechResult.Failed("No local text-to-speech command is configured.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return TextToSpeechResult.Failed("No text was provided for speech synthesis.");
        }

        string outputPath = Path.Combine(Path.GetTempPath(), $"TelegramMessagingTool_TTS_{Guid.NewGuid():N}{_outputExtension}");
        var startInfo = new ProcessStartInfo
        {
            FileName = _command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        string renderedArguments = _argumentsTemplate
            .Replace("{text}", text, StringComparison.OrdinalIgnoreCase)
            .Replace("{output}", outputPath, StringComparison.OrdinalIgnoreCase);
        foreach (string argument in LocalCommandAudioTranscriptionService.SplitArguments(renderedArguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start local text-to-speech command.");
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
                return TextToSpeechResult.Failed($"Local text-to-speech provider exited with code {process.ExitCode}. {Truncate(detail)}".Trim());
            }

            if (!File.Exists(outputPath))
            {
                return TextToSpeechResult.Failed("Local text-to-speech provider did not create the expected output file.");
            }

            byte[] audioBytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            if (audioBytes.Length == 0)
            {
                return TextToSpeechResult.Failed("Local text-to-speech provider returned an empty audio file.");
            }

            return TextToSpeechResult.Ok(audioBytes, _outputExtension, string.IsNullOrWhiteSpace(output) ? "Local text-to-speech provider generated audio." : output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TextToSpeechResult.Failed("Local text-to-speech provider timed out.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return TextToSpeechResult.Failed($"Local text-to-speech provider could not be started: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
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
