using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramMessagingTool.Commands;

public sealed record CommandResult(bool Handled, string? ReplyText, InlineKeyboardMarkup? ReplyMarkup = null);
