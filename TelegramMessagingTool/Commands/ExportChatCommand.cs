using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class ExportChatCommand : IBotCommand
{
    private const int DefaultMessageCount = 100;
    private const int MaxMessageCount = 500;
    private readonly DocumentStorageService _documentStorage;

    public ExportChatCommand(DocumentStorageService documentStorage)
    {
        _documentStorage = documentStorage;
    }

    public string Name => "/exportchat";

    public string Description => "Export recent chat history as a sandboxed TXT, DOCX, or PDF file.";

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

        ExportChatRequest request = Parse(CommandParser.GetArguments(messageText, Name));
        if (!IsSupportedFormat(request.Format))
        {
            return new CommandResult(true, "This export phase supports TXT, DOCX, or PDF only. Usage: /exportchat [txt|docx|pdf] [last N]");
        }

        List<ChatMessage> recentMessages = await dbContext.Messages
            .AsNoTracking()
            .Where(x => x.ConnectedUserId == user.Id && x.ChatId == user.ChatId)
            .OrderByDescending(x => x.Timestamp)
            .Take(request.Count)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(cancellationToken);

        if (recentMessages.Count == 0)
        {
            return new CommandResult(true, "No chat history is available to export yet. Send a normal message first, then try /exportchat txt last 50.");
        }

        string exportText = RenderExportText(user, recentMessages);
        string extension = NormalizeExtension(request.Format);
        string formatLabel = extension.ToUpperInvariant();
        string fileName = $"chat-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.{extension}";
        UploadedFile exportFile = await _documentStorage.CreateTextFileAsync(user, fileName, exportText, cancellationToken);
        dbContext.UploadedFiles.Add(exportFile);
        await dbContext.SaveChangesAsync(cancellationToken);

        string reply = $"Chat export created as #{exportFile.Id}: {exportFile.OriginalFileName}\nFormat: {formatLabel}\nMessages exported: {recentMessages.Count}\nThe {formatLabel} file is attached and also available through /readfile {exportFile.Id}.";
        return new CommandResult(true, reply, DocumentFile: exportFile, ReactionEmoji: "📄");
    }

    private static bool IsSupportedFormat(string format)
    {
        return string.Equals(format, "txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "docx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExtension(string format)
    {
        if (format.Equals("docx", StringComparison.OrdinalIgnoreCase))
        {
            return "docx";
        }

        if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "pdf";
        }

        return "txt";
    }

    private static ExportChatRequest Parse(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return new ExportChatRequest("txt", DefaultMessageCount);
        }

        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string format = parts.Length > 0 ? parts[0].ToLowerInvariant() : "txt";
        int count = DefaultMessageCount;

        for (int i = 1; i < parts.Length; i++)
        {
            string token = parts[i];
            if (string.Equals(token, "last", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
            {
                if (int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLast))
                {
                    count = parsedLast;
                }

                i++;
                continue;
            }

            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount))
            {
                count = parsedCount;
            }
        }

        count = Math.Clamp(count, 1, MaxMessageCount);
        return new ExportChatRequest(format, count);
    }

    private static string RenderExportText(ConnectedUser user, IReadOnlyList<ChatMessage> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine("TelegramMessagingTool chat export");
        builder.AppendLine($"Chat ID: {user.ChatId}");
        builder.AppendLine($"Exported at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine($"Messages: {messages.Count}");
        builder.AppendLine(new string('-', 72));

        foreach (ChatMessage chatMessage in messages)
        {
            builder.Append('[')
                .Append(chatMessage.Timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Append(" UTC] ")
                .Append(chatMessage.Role)
                .AppendLine(":");
            builder.AppendLine(chatMessage.Content);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private sealed record ExportChatRequest(string Format, int Count);
}
