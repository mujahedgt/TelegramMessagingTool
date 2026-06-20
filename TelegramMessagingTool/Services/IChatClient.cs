namespace TelegramMessagingTool.Services;

public interface IChatClient
{
    Task<string> AskAsync(List<OllamaMessageDto> conversationContext, CancellationToken cancellationToken);
}
