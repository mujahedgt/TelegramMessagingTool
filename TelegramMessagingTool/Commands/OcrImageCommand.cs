using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class OcrImageCommand : IBotCommand
{
    private readonly BotSettings _settings;
    private readonly DocumentStorageService _documentStorage;
    private readonly IImageOcrService? _imageOcrService;

    public OcrImageCommand(
        BotSettings settings,
        DocumentStorageService documentStorage,
        IImageOcrService? imageOcrService = null)
    {
        _settings = settings;
        _documentStorage = documentStorage;
        _imageOcrService = imageOcrService;
    }

    public string Name => "/ocrimage";

    public string Description => "Extract readable text from a saved image when a trusted local OCR provider is configured.";

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
            return new CommandResult(true, "Usage: /ocrimage <image-file-id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == imageFileId && x.ConnectedUserId == user.Id, cancellationToken);

        if (file is null)
        {
            return new CommandResult(true, $"Image file #{imageFileId} was not found.");
        }

        if (!DocumentStorageService.IsImageFileName(file.OriginalFileName))
        {
            return new CommandResult(true, $"File #{file.Id} is not an image. Use /images to list saved image files.");
        }

        string absolutePath = Path.GetFullPath(file.AbsolutePath);
        if (!IsInsideSandbox(absolutePath))
        {
            return new CommandResult(true, "Image file is outside the current document sandbox. Re-upload or import it again before OCR extraction.");
        }

        if (!File.Exists(absolutePath))
        {
            return new CommandResult(true, "Image file is missing on disk. Re-upload or import it again before OCR extraction.");
        }

        if (!_settings.EnableImageOcr)
        {
            return new CommandResult(true, "Image OCR is disabled. Set ENABLE_IMAGE_OCR=true only after configuring a trusted local OCR provider.");
        }

        if (_imageOcrService is null)
        {
            return new CommandResult(true, "Image OCR is enabled, but no trusted local OCR provider is configured yet.");
        }

        ImageOcrResult ocrResult = await _imageOcrService.ExtractTextAsync(file, cancellationToken);
        if (!ocrResult.Success)
        {
            return new CommandResult(true, $"OCR extraction failed:\n{ocrResult.Output}");
        }

        string ocrText = ocrResult.Output.Trim();
        if (string.IsNullOrWhiteSpace(ocrText))
        {
            return new CommandResult(true, "The OCR provider returned empty text.");
        }

        UploadedFile ocrFile;
        try
        {
            ocrFile = await _documentStorage.CreateTextFileAsync(
                user,
                $"{file.OriginalFileName}-ocr.txt",
                ocrText,
                cancellationToken);
            ocrFile.Source = "ocr";
            dbContext.UploadedFiles.Add(ocrFile);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new CommandResult(true, $"OCR text was extracted but could not be saved as a document: {ex.Message}");
        }

        return new CommandResult(true, $"OCR text for #{file.Id} {file.OriginalFileName}:\n\n{ocrText}\n\nSaved OCR text file: #{ocrFile.Id} {ocrFile.OriginalFileName}\nUse /readfile {ocrFile.Id}, /askfile {ocrFile.Id} <question>, or /indexfile {ocrFile.Id} for follow-up.");
    }

    private bool IsInsideSandbox(string absolutePath)
    {
        string root = Path.GetFullPath(_documentStorage.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return absolutePath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
