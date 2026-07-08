namespace TelegramMessagingTool.Services.Vector;

public sealed record VectorStoreFactoryResult(string Provider, IVectorStore? VectorStore);

public static class VectorStoreFactory
{
    public static VectorStoreFactoryResult Create(string provider, string localJsonPath)
    {
        string normalizedProvider = BotConfiguration.NormalizeVectorStoreProvider(provider);
        IVectorStore? vectorStore = normalizedProvider switch
        {
            "local_json" => new LocalJsonVectorStore(localJsonPath),
            _ => null
        };

        return new VectorStoreFactoryResult(normalizedProvider, vectorStore);
    }
}
