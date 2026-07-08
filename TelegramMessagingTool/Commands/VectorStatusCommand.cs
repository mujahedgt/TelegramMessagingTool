using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class VectorStatusCommand : IBotCommand
{
    private readonly BotSettings _settings;

    public VectorStatusCommand(BotSettings settings)
    {
        _settings = settings;
    }

    public string Name => "/vectorstatus";

    public string Description => "Show vector-store provider status for document retrieval.";

    public Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return Task.FromResult(new CommandResult(false, null));
        }

        string provider = _settings.VectorStoreProvider;
        string runtime = string.Equals(provider, "local_json", StringComparison.OrdinalIgnoreCase)
            ? "local JSON vector store is configured; document embeddings can be mirrored there when /embedfile, /embeddocs, or /reembeddocs runs."
            : "SQL DocumentChunk.EmbeddingJson is the active/default retrieval path.";

        string reply = $"""
Vector store status:
- Provider: {provider}
- Local path: {_settings.VectorStorePath}
- Runtime: {runtime}
- Retrieval safety: searches stay scoped to the current Telegram chat/user before ranking.
""";

        return Task.FromResult(new CommandResult(true, reply.Trim()));
    }
}
