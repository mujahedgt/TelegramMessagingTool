namespace TelegramMessagingTool.Commands;

public sealed record CommandResult(bool Handled, string? ReplyText);
