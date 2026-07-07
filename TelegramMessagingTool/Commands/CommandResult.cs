using Telegram.Bot.Types.ReplyMarkups;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed record CommandResult(
    bool Handled,
    string? ReplyText,
    InlineKeyboardMarkup? ReplyMarkup = null,
    UploadedFile? AudioFile = null,
    bool SendAudioAsVoice = false,
    string? ReactionEmoji = null,
    UploadedFile? DocumentFile = null);
