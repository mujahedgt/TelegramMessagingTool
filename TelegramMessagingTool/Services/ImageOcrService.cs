using System.Diagnostics;

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

        LocalCommandProcessSupport.AddTemplateArguments(
            startInfo,
            _argumentsTemplate,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["{file}"] = imagePath });

        try
        {
            LocalCommandProcessResult processResult = await LocalCommandProcessSupport.RunAsync(startInfo, _timeout, cancellationToken);
            if (processResult.TimedOut)
            {
                return ImageOcrResult.Failed("Local image OCR provider timed out.");
            }

            string output = processResult.Output;
            string error = processResult.Error;
            if (processResult.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(error) ? output : error;
                return ImageOcrResult.Failed($"Local image OCR provider exited with code {processResult.ExitCode}. {LocalCommandProcessSupport.Truncate(detail)}".Trim());
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                return ImageOcrResult.Failed("Local image OCR provider returned empty text.");
            }

            return ImageOcrResult.Ok(LocalCommandProcessSupport.Truncate(output));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ImageOcrResult.Failed($"Local image OCR provider could not be started: {ex.Message}");
        }
    }

    internal static IReadOnlyList<string> SplitArguments(string arguments) => LocalCommandProcessSupport.SplitArguments(arguments);
}
