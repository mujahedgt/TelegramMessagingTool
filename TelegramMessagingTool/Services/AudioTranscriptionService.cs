using System.Diagnostics;
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

        LocalCommandProcessSupport.AddTemplateArguments(
            startInfo,
            _argumentsTemplate,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["{file}"] = audioPath });

        try
        {
            LocalCommandProcessResult processResult = await LocalCommandProcessSupport.RunAsync(startInfo, _timeout, cancellationToken);
            if (processResult.TimedOut)
            {
                return AudioTranscriptionResult.Failed("Local transcription provider timed out.");
            }

            string output = processResult.Output;
            string error = processResult.Error;
            if (processResult.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(error) ? output : error;
                return AudioTranscriptionResult.Failed($"Local transcription provider exited with code {processResult.ExitCode}. {LocalCommandProcessSupport.Truncate(detail)}".Trim());
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                return AudioTranscriptionResult.Failed("Local transcription provider returned an empty transcript.");
            }

            return AudioTranscriptionResult.Ok(LocalCommandProcessSupport.Truncate(output));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return AudioTranscriptionResult.Failed($"Local transcription provider could not be started: {ex.Message}");
        }
    }

    internal static IReadOnlyList<string> SplitArguments(string arguments) => LocalCommandProcessSupport.SplitArguments(arguments);
}
