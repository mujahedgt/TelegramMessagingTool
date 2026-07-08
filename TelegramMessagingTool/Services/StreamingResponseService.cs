namespace TelegramMessagingTool.Services;

public sealed class StreamingResponseService
{
    private readonly IStreamingChatClient _streamingChatClient;
    private readonly IChatClient _fallbackChatClient;

    public StreamingResponseService(IStreamingChatClient streamingChatClient, IChatClient fallbackChatClient)
    {
        _streamingChatClient = streamingChatClient;
        _fallbackChatClient = fallbackChatClient;
    }

    public async Task<string> GenerateAsync(
        List<OllamaMessageDto> conversationContext,
        bool streamingEnabled,
        Func<string, CancellationToken, Task>? onDeltaAsync,
        CancellationToken cancellationToken,
        ModelTaskKind taskKind = ModelTaskKind.Chat)
    {
        if (!streamingEnabled)
        {
            return await _fallbackChatClient.AskAsync(conversationContext, cancellationToken, taskKind);
        }

        try
        {
            string streamedAnswer = await _streamingChatClient.AskStreamingAsync(
                conversationContext,
                onDeltaAsync,
                cancellationToken,
                taskKind);

            if (!IsStreamingFailure(streamedAnswer))
            {
                return streamedAnswer;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall through to the non-streaming path. Streaming is an optional UX optimization.
        }

        return await _fallbackChatClient.AskAsync(conversationContext, cancellationToken, taskKind);
    }

    public static bool IsStreamingFailure(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return true;
        }

        return answer.StartsWith("Invalid response received from Ollama.", StringComparison.OrdinalIgnoreCase)
            || answer.StartsWith("Ollama returned an error:", StringComparison.OrdinalIgnoreCase)
            || answer.StartsWith("Empty response from Ollama.", StringComparison.OrdinalIgnoreCase);
    }
}
