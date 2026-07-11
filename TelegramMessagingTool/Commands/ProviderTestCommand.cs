using System.Diagnostics;
using System.Text;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class ProviderTestCommand : IBotCommand
{
    private readonly BotSettings _settings;
    private readonly IProviderCommandRunner _runner;

    public ProviderTestCommand(BotSettings settings, IProviderCommandRunner? runner = null)
    {
        _settings = settings;
        _runner = runner ?? new SystemProviderCommandRunner();
    }

    public string Name => "/providertest";

    public string Description => "Admin-only: run a safe local media provider readiness test.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!CommandParser.Matches(message.Text, Name))
        {
            return new CommandResult(false, null);
        }

        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return new CommandResult(true, BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId));
        }

        string provider = CommandParser.GetArguments(message.Text ?? string.Empty, Name).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(provider))
        {
            return new CommandResult(true, "Provider test\n\nUsage: /providertest ocr|stt|tts\nRuns the configured local provider with temporary non-secret sample inputs.");
        }

        ProviderTestSpec spec = provider switch
        {
            "ocr" or "image_ocr" => ProviderTestSpec.FileInput("Image OCR", _settings.EnableImageOcr, _settings.ImageOcrCommand, _settings.ImageOcrArguments, _settings.ImageOcrTimeoutSeconds, ".txt", "sample image placeholder"),
            "stt" or "audio" or "transcription" => ProviderTestSpec.FileInput("Audio transcription", _settings.EnableAudioTranscription, _settings.AudioTranscriptionCommand, _settings.AudioTranscriptionArguments, _settings.AudioTranscriptionTimeoutSeconds, ".txt", "sample audio placeholder"),
            "tts" or "speech" => ProviderTestSpec.TextToSpeech(_settings.EnableTextToSpeech, _settings.TextToSpeechCommand, _settings.TextToSpeechArguments, _settings.TextToSpeechTimeoutSeconds, _settings.TextToSpeechOutputExtension),
            _ => ProviderTestSpec.Invalid(provider)
        };

        if (!spec.Valid)
        {
            return new CommandResult(true, $"Provider test\n\nUnknown provider '{provider}'. Use: ocr, stt, or tts.");
        }

        if (!spec.Enabled)
        {
            return new CommandResult(true, $"Provider test: {spec.DisplayName}\n\nStatus: disabled by feature flag. Enable the matching ENABLE_* setting and restart the latest release.");
        }

        if (string.IsNullOrWhiteSpace(spec.Command))
        {
            return new CommandResult(true, $"Provider test: {spec.DisplayName}\n\nStatus: provider command is missing. Configure the matching *_COMMAND Windows User environment variable.");
        }

        ProviderCommandRunResult result = await _runner.RunAsync(spec, cancellationToken);
        string status = result.Success ? "OK" : "FAILED";
        var builder = new StringBuilder();
        builder.AppendLine($"Provider test: {spec.DisplayName}");
        builder.AppendLine();
        builder.AppendLine($"Status: {status}");
        builder.AppendLine($"Exit code: {result.ExitCode}");
        builder.AppendLine($"Duration: {result.Duration.TotalMilliseconds:0} ms");
        if (!string.IsNullOrWhiteSpace(result.OutputPreview))
        {
            builder.AppendLine($"Output: {result.OutputPreview}");
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorPreview))
        {
            builder.AppendLine($"Error: {result.ErrorPreview}");
        }

        builder.AppendLine();
        builder.AppendLine("Secrets: command path and raw arguments are not shown.");
        return new CommandResult(true, builder.ToString().TrimEnd());
    }
}

public sealed record ProviderTestSpec(
    bool Valid,
    string DisplayName,
    bool Enabled,
    string Command,
    string ArgumentsTemplate,
    int TimeoutSeconds,
    string? InputFileExtension,
    string? InputFileContent,
    string? TextInput,
    string? OutputExtension)
{
    public static ProviderTestSpec Invalid(string provider) => new(false, provider, false, string.Empty, string.Empty, 0, null, null, null, null);

    public static ProviderTestSpec FileInput(string displayName, bool enabled, string command, string arguments, int timeoutSeconds, string extension, string content)
        => new(true, displayName, enabled, command, arguments, timeoutSeconds, extension, content, null, null);

    public static ProviderTestSpec TextToSpeech(bool enabled, string command, string arguments, int timeoutSeconds, string outputExtension)
        => new(true, "Text-to-speech", enabled, command, arguments, timeoutSeconds, null, null, "Provider readiness test from TelegramMessagingTool.", outputExtension);
}

public interface IProviderCommandRunner
{
    Task<ProviderCommandRunResult> RunAsync(ProviderTestSpec spec, CancellationToken cancellationToken);
}

public sealed record ProviderCommandRunResult(bool Success, int ExitCode, TimeSpan Duration, string OutputPreview, string ErrorPreview);

public sealed class SystemProviderCommandRunner : IProviderCommandRunner
{
    public async Task<ProviderCommandRunResult> RunAsync(ProviderTestSpec spec, CancellationToken cancellationToken)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "TelegramMessagingTool_ProviderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string inputFile = string.Empty;
        string outputFile = string.Empty;
        try
        {
            if (!string.IsNullOrWhiteSpace(spec.InputFileExtension))
            {
                inputFile = Path.Combine(tempDirectory, "input" + spec.InputFileExtension);
                await File.WriteAllTextAsync(inputFile, spec.InputFileContent ?? string.Empty, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(spec.OutputExtension))
            {
                outputFile = Path.Combine(tempDirectory, "output" + spec.OutputExtension);
            }

            string arguments = (spec.ArgumentsTemplate ?? string.Empty)
                .Replace("{file}", inputFile, StringComparison.OrdinalIgnoreCase)
                .Replace("{text}", spec.TextInput ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("{output}", outputFile, StringComparison.OrdinalIgnoreCase);

            var startInfo = new ProcessStartInfo
            {
                FileName = spec.Command,
                Arguments = arguments,
                WorkingDirectory = tempDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var stopwatch = Stopwatch.StartNew();
            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            Task delayTask = Task.Delay(TimeSpan.FromSeconds(Math.Clamp(spec.TimeoutSeconds, 5, 300)), cancellationToken);
            Task exitTask = process.WaitForExitAsync(cancellationToken);
            if (await Task.WhenAny(exitTask, delayTask) == delayTask)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new ProviderCommandRunResult(false, -1, stopwatch.Elapsed, string.Empty, "Provider command timed out.");
            }

            string output = await outputTask;
            string error = await errorTask;
            bool outputCreated = string.IsNullOrWhiteSpace(outputFile) || File.Exists(outputFile);
            return new ProviderCommandRunResult(process.ExitCode == 0 && outputCreated, process.ExitCode, stopwatch.Elapsed, Preview(output), Preview(error));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new ProviderCommandRunResult(false, -1, TimeSpan.Zero, string.Empty, Preview(ex.Message));
        }
        finally
        {
            try { if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true); } catch { }
        }
    }

    private static string Preview(string text)
    {
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= 500 ? text : text[..500] + "...";
    }
}
