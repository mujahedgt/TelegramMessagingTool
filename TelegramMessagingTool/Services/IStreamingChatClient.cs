namespace TelegramMessagingTool.Services;

public interface IStreamingChatClient
{
    Task<string> AskStreamingAsync(
        List<OllamaMessageDto> conversationContext,
        Func<string, CancellationToken, Task>? onDeltaAsync,
        CancellationToken cancellationToken,
        ModelTaskKind taskKind = ModelTaskKind.Chat);
}
