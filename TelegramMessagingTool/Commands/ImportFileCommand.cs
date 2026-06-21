using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ImportFileCommand : IBotCommand
{
    private readonly string _importDirectory;
    private readonly DocumentStorageService _documentStorage;
    private readonly BotSettings _settings;

    public ImportFileCommand(string importDirectory, DocumentStorageService documentStorage, BotSettings settings)
    {
        _importDirectory = Path.GetFullPath(importDirectory);
        _documentStorage = documentStorage;
        _settings = settings;
        Directory.CreateDirectory(_importDirectory);
    }

    public string Name => "/importfile";

    public string Description => "Admin-only: copy one file from local ImportInbox into your document sandbox.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.Equals("/importfile", StringComparison.OrdinalIgnoreCase)
            && !messageText.StartsWith("/importfile ", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return new CommandResult(true, BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId));
        }

        string requestedFileName = messageText["/importfile".Length..].Trim();
        if (string.IsNullOrWhiteSpace(requestedFileName))
        {
            return new CommandResult(true, "Usage: /importfile <filename-from-ImportInbox>");
        }

        string safeName = DocumentStorageService.SanitizeFileName(requestedFileName);
        if (!string.Equals(safeName, requestedFileName, StringComparison.Ordinal))
        {
            return new CommandResult(true, "Import only accepts a plain filename from ImportInbox, not a path.");
        }

        if (!_documentStorage.IsAllowedFileName(safeName))
        {
            return new CommandResult(true, $"Unsupported document type. Allowed: {_documentStorage.AllowedExtensionsText}");
        }

        string sourcePath = Path.GetFullPath(Path.Combine(_importDirectory, safeName));
        if (!sourcePath.StartsWith(_importDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(true, "Resolved import path escaped ImportInbox. Refusing import.");
        }

        if (!File.Exists(sourcePath))
        {
            return new CommandResult(true, $"Import file was not found: {safeName}\nUse /importfiles to list available files.");
        }

        long size = new FileInfo(sourcePath).Length;
        if (size > _documentStorage.MaxFileBytes)
        {
            return new CommandResult(true, $"Import file is too large. Size: {LocalDeviceInfoService.FormatBytes(size)}. Maximum: {LocalDeviceInfoService.FormatBytes(_documentStorage.MaxFileBytes)}.");
        }

        await using FileStream source = File.OpenRead(sourcePath);
        UploadedFile importedFile = await _documentStorage.SaveUploadedFileAsync(
            user,
            safeName,
            telegramFileId: string.Empty,
            contentType: string.Empty,
            source,
            size,
            cancellationToken);

        importedFile.Source = "local_import";
        dbContext.UploadedFiles.Add(importedFile);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CommandResult(true, $"Imported file as #{importedFile.Id}: {importedFile.OriginalFileName} ({LocalDeviceInfoService.FormatBytes(importedFile.SizeBytes)})\nUse /readfile {importedFile.Id}, /indexfile {importedFile.Id}, or /askfile {importedFile.Id} <question>.\nOriginal ImportInbox file was left in place.");
    }
}
