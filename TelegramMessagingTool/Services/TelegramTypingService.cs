using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramMessagingTool.ConsoleUi;

namespace TelegramMessagingTool.Services;

public sealed class TelegramTypingService
{
    private static readonly TimeSpan DefaultTypingInterval = TimeSpan.FromSeconds(4);

    private readonly Action<string, string, string, ConsoleEventLevel> _writeConsoleEvent;

    public TelegramTypingService(Action<string, string, string, ConsoleEventLevel>? writeConsoleEvent = null)
    {
        _writeConsoleEvent = writeConsoleEvent ?? ((_, _, _, _) => { });
    }

    public static bool ShouldSendTypingIndicator(BotSettings settings, bool isCommand)
    {
        return settings.EnableTelegramTypingIndicator && !isCommand;
    }

    public Task<T> RunWithTypingAsync<T>(
        ITelegramBotClient bot,
        long chatId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return RunWithTypingLoopAsync(
            async token =>
            {
                try
                {
                    await bot.SendChatAction(
                        chatId: chatId,
                        action: ChatAction.Typing,
                        cancellationToken: token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _writeConsoleEvent("NET", chatId.ToString(), $"typing indicator failed: {ex.Message}", ConsoleEventLevel.Warning);
                }
            },
            operation,
            DefaultTypingInterval,
            cancellationToken);
    }

    public static async Task<T> RunWithTypingLoopAsync<T>(
        Func<CancellationToken, Task> sendTypingAsync,
        Func<CancellationToken, Task<T>> operation,
        TimeSpan typingInterval,
        CancellationToken cancellationToken)
    {
        if (typingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(typingInterval), "Typing interval must be positive.");
        }

        using var typingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task typingLoop = RunTypingLoopAsync(sendTypingAsync, typingInterval, typingCancellation.Token);

        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            await typingCancellation.CancelAsync();
            try
            {
                await typingLoop;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path for the typing loop.
            }
        }
    }

    private static async Task RunTypingLoopAsync(
        Func<CancellationToken, Task> sendTypingAsync,
        TimeSpan typingInterval,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await sendTypingAsync(cancellationToken);
            await Task.Delay(typingInterval, cancellationToken);
        }
    }
}
