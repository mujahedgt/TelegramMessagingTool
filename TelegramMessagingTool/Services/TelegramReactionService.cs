using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using TelegramMessagingTool.ConsoleUi;

namespace TelegramMessagingTool.Services;

public sealed class TelegramReactionService
{
    private readonly Action<string, string, string, ConsoleEventLevel> _writeConsoleEvent;

    public TelegramReactionService(Action<string, string, string, ConsoleEventLevel>? writeConsoleEvent = null)
    {
        _writeConsoleEvent = writeConsoleEvent ?? ((_, _, _, _) => { });
    }

    public async Task TrySendReactionAsync(
        ITelegramBotClient bot,
        ChatId chatId,
        int messageId,
        string? emoji,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emoji))
        {
            return;
        }

        string normalizedEmoji = emoji.Trim();
        if (!IsSupportedReactionEmoji(normalizedEmoji))
        {
            _writeConsoleEvent("REACTION", chatId.ToString() ?? "unknown", "unsupported reaction metadata ignored", ConsoleEventLevel.Warning);
            return;
        }

        try
        {
            await bot.SetMessageReaction(
                chatId: chatId,
                messageId: messageId,
                reaction: [normalizedEmoji],
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApiRequestException ex)
        {
            _writeConsoleEvent("REACTION", chatId.ToString() ?? "unknown", ex.Message, ConsoleEventLevel.Warning);
        }
        catch (HttpRequestException ex)
        {
            _writeConsoleEvent("REACTION", chatId.ToString() ?? "unknown", ex.Message, ConsoleEventLevel.Warning);
        }
    }

    public static bool IsSupportedReactionEmoji(string emoji)
    {
        return emoji is "👍" or "✅" or "🧹" or "👎";
    }
}
