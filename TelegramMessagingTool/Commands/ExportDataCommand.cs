using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ExportDataCommand : IBotCommand
{
    private readonly BackupExportService _backupExportService;

    public ExportDataCommand(BackupExportService backupExportService)
    {
        _backupExportService = backupExportService;
    }

    public string Name => "/exportdata";

    public string Description => "Export current chat/user data as a sandboxed JSON backup file.";

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

        string args = CommandParser.GetArguments(messageText, Name).Trim();
        if (!string.IsNullOrWhiteSpace(args) && !string.Equals(args, "json", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(true, "Usage: /exportdata [json]. This backup/export phase supports JSON only.");
        }

        BackupExportResult result = await _backupExportService.ExportUserDataAsync(dbContext, user, cancellationToken);
        string reply = $"""
Data export created as #{result.File.Id}: {result.File.OriginalFileName}
Format: JSON
Messages: {result.MessageCount}
Memories: {result.MemoryCount}
Files: {result.FileCount}
Document chunks: {result.DocumentChunkCount}
Tasks: {result.TaskCount}
Actions: {result.PendingActionCount}
The JSON file is attached and also available through /readfile {result.File.Id}.
""";

        return new CommandResult(true, reply, DocumentFile: result.File, ReactionEmoji: "💾");
    }
}
