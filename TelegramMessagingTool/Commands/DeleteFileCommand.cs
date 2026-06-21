using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class DeleteFileCommand : IBotCommand
{
    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;

    public DeleteFileCommand(PendingActionService pendingActionService, BotSettings settings)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
    }

    public string Name => "/deletefile";

    public string Description => "Admin-only: create an approval request to delete a sandboxed saved file.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!messageText.StartsWith("/deletefile", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, null);
        }

        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return new CommandResult(true, BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId));
        }

        string idText = messageText["/deletefile".Length..].Trim();
        if (!int.TryParse(idText, out int fileId) || fileId <= 0)
        {
            return new CommandResult(true, "Usage: /deletefile <file id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == fileId && x.ConnectedUserId == user.Id, cancellationToken);

        if (file is null)
        {
            return new CommandResult(true, $"File #{fileId} was not found.");
        }

        string payloadJson = JsonSerializer.Serialize(new { file_id = file.Id, original_file_name = file.OriginalFileName });
        PendingAction action = await _pendingActionService.CreateAsync(
            dbContext,
            user,
            "delete_file",
            $"Delete sandboxed file #{file.Id}: {file.OriginalFileName}.",
            payloadJson,
            "high",
            TimeSpan.FromMinutes(10),
            cancellationToken);

        return new CommandResult(true, $"""
Delete-file approval request created.

Action #{action.Id}: delete_file
File: #{file.Id} {file.OriginalFileName}
Risk: high
Expires: {action.ExpiresAt:u}

Review with /action {action.Id}
Approve with /approve {action.Id}
Deny with /deny {action.Id}
""");
    }
}
