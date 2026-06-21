using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ImportFilesCommand : IBotCommand
{
    private readonly string _importDirectory;
    private readonly DocumentStorageService _documentStorage;
    private readonly BotSettings _settings;

    public ImportFilesCommand(string importDirectory, DocumentStorageService documentStorage, BotSettings settings)
    {
        _importDirectory = Path.GetFullPath(importDirectory);
        _documentStorage = documentStorage;
        _settings = settings;
        Directory.CreateDirectory(_importDirectory);
    }

    public string Name => "/importfiles";

    public string Description => "Admin-only: list files available in the local ImportInbox folder.";

    public Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return Task.FromResult(new CommandResult(false, null));
        }

        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return Task.FromResult(new CommandResult(true, BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId)));
        }

        Directory.CreateDirectory(_importDirectory);
        List<FileInfo> files = Directory.EnumerateFiles(_importDirectory)
            .Select(x => new FileInfo(x))
            .Where(x => _documentStorage.IsAllowedFileName(x.Name))
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .Take(20)
            .ToList();

        if (files.Count == 0)
        {
            return Task.FromResult(new CommandResult(true, $"No importable files found. Put .txt/.md/.json/.csv/.pdf/.docx/.xlsx files in:\n{_importDirectory}"));
        }

        string reply = "ImportInbox files:\n" + string.Join("\n", files.Select(x =>
        {
            string size = LocalDeviceInfoService.FormatBytes(x.Length);
            string marker = x.Length > _documentStorage.MaxFileBytes ? " - too large" : string.Empty;
            return $"- {x.Name} ({size}){marker}";
        })) + "\n\nImport with: /importfile <filename>";

        return Task.FromResult(new CommandResult(true, reply));
    }
}
