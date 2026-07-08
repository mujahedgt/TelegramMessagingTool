namespace TelegramMessagingTool.Services.Vector;

public sealed record VectorStoreFactoryResult(string Provider, IVectorStore? VectorStore);

public static class VectorStoreFactory
{
    public static VectorStoreFactoryResult Create(
        string provider,
        string localJsonPath,
        string? qdrantUrl = null,
        string? qdrantCollection = null,
        HttpClient? qdrantHttpClient = null)
    {
        string normalizedProvider = BotConfiguration.NormalizeVectorStoreProvider(provider);
        IVectorStore? vectorStore = normalizedProvider switch
        {
            "local_json" => new LocalJsonVectorStore(localJsonPath),
            "qdrant" => new QdrantVectorStore(
                qdrantHttpClient ?? new HttpClient(),
                BotConfiguration.NormalizeQdrantUrl(qdrantUrl),
                BotConfiguration.NormalizeQdrantCollection(qdrantCollection)),
            _ => null
        };

        return new VectorStoreFactoryResult(normalizedProvider, vectorStore);
    }
}
