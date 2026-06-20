using System.Net.Http;
using System.Net.Sockets;
using Telegram.Bot.Exceptions;

namespace TelegramMessagingTool;

public static class TelegramReceiverErrorClassifier
{
    public static bool IsTransientNetworkError(Exception exception)
    {
        return Walk(exception).Any(current =>
            current is RequestException or HttpRequestException or IOException or SocketException or TimeoutException
            || current.Message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase)
            || current.Message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
            || current.Message.Contains("connection timed out", StringComparison.OrdinalIgnoreCase)
            || current.Message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase));
    }

    public static string Summarize(Exception exception)
    {
        if (IsTransientNetworkError(exception))
        {
            Exception deepest = Walk(exception).Last();
            return $"Transient Telegram network error: {deepest.Message}. Long polling will continue automatically.";
        }

        return exception switch
        {
            ApiRequestException apiEx => $"Telegram API error [{apiEx.ErrorCode}]: {apiEx.Message}",
            RequestException requestEx => $"Telegram request error: {requestEx.Message}",
            _ => $"Telegram receiver error: {exception.Message}"
        };
    }

    private static IEnumerable<Exception> Walk(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
