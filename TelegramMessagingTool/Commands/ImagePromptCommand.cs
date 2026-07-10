using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ImagePromptCommand : IBotCommand
{
    private readonly BotSettings _settings;
    private readonly DocumentStorageService _documentStorage;
    private readonly ImagePromptService _imagePromptService;
    private readonly IImageDescriptionService? _imageDescriptionService;

    public ImagePromptCommand(
        BotSettings settings,
        DocumentStorageService documentStorage,
        ImagePromptService imagePromptService,
        IImageDescriptionService? imageDescriptionService = null)
    {
        _settings = settings;
        _documentStorage = documentStorage;
        _imagePromptService = imagePromptService;
        _imageDescriptionService = imageDescriptionService;
    }

    public string Name => "/imageprompt";

    public string Description => "Draft a safe image-generation prompt from a saved image or text idea without generating an image.";

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

        string input = CommandParser.GetArguments(messageText, Name).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return new CommandResult(true, "Usage: /imageprompt <image-file-id|idea>");
        }

        if (int.TryParse(input, out int imageFileId))
        {
            return await GenerateFromImageFileAsync(imageFileId, user, dbContext, cancellationToken);
        }

        string promptDraft = await _imagePromptService.GenerateFromIdeaAsync(input, cancellationToken);
        return new CommandResult(true, $"Image prompt draft:\n\n{promptDraft}\n\nThis command drafts prompt text only; it does not generate or send images.");
    }

    private async Task<CommandResult> GenerateFromImageFileAsync(
        int imageFileId,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == imageFileId && x.ConnectedUserId == user.Id, cancellationToken);

        if (file is null)
        {
            return new CommandResult(true, $"Image file #{imageFileId} was not found.");
        }

        if (!DocumentStorageService.IsImageFileName(file.OriginalFileName)
            && !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(true, $"File #{file.Id} is not an image. Use /images to list saved image files.");
        }

        string absolutePath = Path.GetFullPath(file.AbsolutePath);
        if (!IsInsideSandbox(absolutePath))
        {
            return new CommandResult(true, "Image file is outside the current document sandbox. Re-upload or import it again before prompt drafting from an image.");
        }

        if (!File.Exists(absolutePath))
        {
            return new CommandResult(true, "Image file is missing on disk. Re-upload or import it again before prompt drafting from an image.");
        }

        string imageContext = BuildMetadataContext(file);
        if (_settings.EnableImageVision && _imageDescriptionService is not null)
        {
            ImageDescriptionResult description = await _imageDescriptionService.DescribeAsync(file, _settings.ImageDescriptionPrompt, cancellationToken);
            if (description.Success && !string.IsNullOrWhiteSpace(description.Output))
            {
                imageContext = description.Output.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(description.Output))
            {
                imageContext += $"\nVision analysis note: {description.Output.Trim()}";
            }
        }

        string promptDraft = await _imagePromptService.GenerateFromImageContextAsync(
            imageContext,
            $"#{file.Id} {file.OriginalFileName}",
            cancellationToken);

        return new CommandResult(true, $"Image prompt for #{file.Id} {file.OriginalFileName}:\n\n{promptDraft}\n\nThis command drafts prompt text only; it does not generate or send images.");
    }

    private static string BuildMetadataContext(UploadedFile file)
    {
        return $"Saved image metadata only. Filename: {file.OriginalFileName}. Content type: {file.ContentType}. Size: {file.SizeBytes} bytes. Source: {file.Source}.";
    }

    private bool IsInsideSandbox(string absolutePath)
    {
        string root = Path.GetFullPath(_documentStorage.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return absolutePath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
