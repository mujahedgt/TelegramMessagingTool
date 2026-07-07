using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class SendAudioCommand : IBotCommand
{
    private readonly DocumentStorageService _documentStorage;

    public SendAudioCommand(DocumentStorageService documentStorage)
    {
        _documentStorage = documentStorage;
    }

    public string Name => "/sendaudio";

    public string Description => "Send a saved sandboxed audio file back to Telegram.";

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
        if (!int.TryParse(idText, out int audioFileId))
        {
            return new CommandResult(true, "Usage: /sendaudio <audio-file-id>");
        }

        UploadedFile? file = await dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == audioFileId && x.ConnectedUserId == user.Id, cancellationToken);

        if (file is null)
        {
            return new CommandResult(true, $"Audio file #{audioFileId} was not found.");
        }

        if (!DocumentStorageService.IsAudioFileName(file.OriginalFileName)
            && !file.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(true, $"File #{file.Id} is not an audio file. Use /voicefiles to list saved audio files.");
        }

        string absolutePath = Path.GetFullPath(file.AbsolutePath);
        if (!IsInsideSandbox(absolutePath))
        {
            return new CommandResult(true, "Audio file is outside the current document sandbox. Re-upload or import it again before sending.");
        }

        if (!File.Exists(absolutePath))
        {
            return new CommandResult(true, "Audio file is missing on disk. Re-upload or regenerate it before sending.");
        }

        bool sendAsVoice = VoiceMessageProcessor.IsTelegramVoiceExtension(Path.GetExtension(file.OriginalFileName));
        string reply = sendAsVoice
            ? $"Sending audio #{file.Id} as a Telegram voice note: {file.OriginalFileName}"
            : $"Sending audio #{file.Id} as a Telegram audio file: {file.OriginalFileName}";

        return new CommandResult(true, reply, AudioFile: file, SendAudioAsVoice: sendAsVoice);
    }

    private bool IsInsideSandbox(string absolutePath)
    {
        string root = Path.GetFullPath(_documentStorage.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return absolutePath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || absolutePath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
