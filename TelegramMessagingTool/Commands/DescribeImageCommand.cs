using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class DescribeImageCommand : IBotCommand
{
    private readonly BotSettings _settings;
    private readonly DocumentStorageService _documentStorage;
    private readonly IImageDescriptionService? _imageDescriptionService;

    public DescribeImageCommand(
        BotSettings settings,
        DocumentStorageService documentStorage,
        IImageDescriptionService? imageDescriptionService = null)
    {
        _settings = settings;
        _documentStorage = documentStorage;
        _imageDescriptionService = imageDescriptionService;
    }

    public string Name => "/describeimage";

    public string Description => "Show metadata for a saved image and optionally describe it with the image model route.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        string idText = CommandParser.GetArguments(messageText, Name);
        if (!int.TryParse(idText, out int imageFileId))
        {
            return new CommandResult(true, "Usage: /describeimage <image-file-id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == imageFileId && x.ConnectedUserId == user.Id, cancellationToken);

        if (file is null)
        {
            return new CommandResult(true, $"Image file #{imageFileId} was not found.");
        }

        if (!DocumentStorageService.IsImageFileName(file.OriginalFileName)
            && !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(true, $"File #{file.Id} is not an image. Use /files to list saved files or /images to list saved image files.");
        }

        string absolutePath = Path.GetFullPath(file.AbsolutePath);
        bool insideSandbox = IsInsideSandbox(absolutePath);
        string diskStatus = File.Exists(absolutePath) ? "present" : "missing";
        string reply = $"""
Image #{file.Id}: {file.OriginalFileName}

Metadata:
- Content type: {file.ContentType}
- Size: {file.SizeBytes} bytes
- Source: {file.Source}
- Disk: {diskStatus}
- Image model route: {_settings.OllamaImageModel}
- Image vision: {(_settings.EnableImageVision ? "enabled" : "disabled")}
""";

        if (!insideSandbox)
        {
            return new CommandResult(true, reply + "\n\nImage file is outside the current document sandbox. Re-upload or import it again before vision analysis.");
        }

        if (!_settings.EnableImageVision)
        {
            return new CommandResult(true, reply + "\n\nImage vision is disabled. Set ENABLE_IMAGE_VISION=true to allow local Ollama vision descriptions.");
        }

        if (_imageDescriptionService is null)
        {
            return new CommandResult(true, reply + "\n\nImage vision is enabled, but no image description service is configured.");
        }

        string prompt = _settings.ImageDescriptionPrompt;
        ImageDescriptionResult description = await _imageDescriptionService.DescribeAsync(file, prompt, cancellationToken);
        string label = description.Success ? "Description" : "Vision analysis failed";
        return new CommandResult(true, reply + $"\n\n{label}:\n{description.Output}");
    }

    private bool IsInsideSandbox(string absolutePath)
    {
        string root = Path.GetFullPath(_documentStorage.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return absolutePath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
