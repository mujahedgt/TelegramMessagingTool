using System.Net.Http.Json;
using System.Text.Json;

namespace TelegramMessagingTool.Services.Vector;

public sealed class QdrantVectorStore : IVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _collection;

    public QdrantVectorStore(HttpClient httpClient, string qdrantUrl, string collection)
    {
        _httpClient = httpClient;
        _baseUrl = BotConfiguration.NormalizeQdrantUrl(qdrantUrl);
        _collection = BotConfiguration.NormalizeQdrantCollection(collection);
    }

    public async Task UpsertAsync(DocumentVector vector, CancellationToken cancellationToken)
    {
        var payload = new
        {
            points = new[]
            {
                new
                {
                    id = StablePointId(vector.Id),
                    vector = vector.Embedding,
                    payload = new
                    {
                        vectorId = vector.Id,
                        chatId = vector.ChatId,
                        connectedUserId = vector.ConnectedUserId,
                        uploadedFileId = vector.UploadedFileId,
                        chunkId = vector.ChunkId,
                        chunkNumber = vector.ChunkNumber,
                        originalFileName = vector.OriginalFileName,
                        text = vector.Text
                    }
                }
            }
        };

        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            BuildUrl($"collections/{_collection}/points?wait=true"),
            payload,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        long chatId,
        IReadOnlyList<float> queryEmbedding,
        int limit,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            vector = queryEmbedding,
            limit = Math.Clamp(limit, 1, 50),
            with_payload = true,
            filter = new
            {
                must = new[]
                {
                    new
                    {
                        key = "chatId",
                        match = new { value = chatId }
                    }
                }
            }
        };

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            BuildUrl($"collections/{_collection}/points/search"),
            payload,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("result", out JsonElement result) || result.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<VectorSearchResult> results = [];
        foreach (JsonElement item in result.EnumerateArray())
        {
            if (!item.TryGetProperty("payload", out JsonElement itemPayload))
            {
                continue;
            }

            double score = item.TryGetProperty("score", out JsonElement scoreElement) && scoreElement.TryGetDouble(out double parsedScore)
                ? parsedScore
                : 0;

            DocumentVector? vector = ParsePayload(itemPayload, queryEmbedding);
            if (vector is not null)
            {
                results.Add(new VectorSearchResult(vector, score));
            }
        }

        return results;
    }

    public async Task DeleteByUploadedFileIdAsync(int uploadedFileId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            filter = new
            {
                must = new[]
                {
                    new
                    {
                        key = "uploadedFileId",
                        match = new { value = uploadedFileId }
                    }
                }
            }
        };

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            BuildUrl($"collections/{_collection}/points/delete?wait=true"),
            payload,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private string BuildUrl(string relativePath)
    {
        return $"{_baseUrl}/{relativePath}";
    }

    private static string StablePointId(string id)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..32].ToLowerInvariant();
    }

    private static DocumentVector? ParsePayload(JsonElement payload, IReadOnlyList<float> fallbackEmbedding)
    {
        string vectorId = GetString(payload, "vectorId") ?? string.Empty;
        long chatId = GetInt64(payload, "chatId");
        int connectedUserId = GetInt32(payload, "connectedUserId");
        int uploadedFileId = GetInt32(payload, "uploadedFileId");
        int chunkId = GetInt32(payload, "chunkId");
        int chunkNumber = GetInt32(payload, "chunkNumber");
        string originalFileName = GetString(payload, "originalFileName") ?? string.Empty;
        string text = GetString(payload, "text") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(vectorId) || chatId == 0 || chunkId == 0)
        {
            return null;
        }

        return new DocumentVector(vectorId, chatId, connectedUserId, uploadedFileId, chunkId, chunkNumber, originalFileName, text, fallbackEmbedding);
    }

    private static string? GetString(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int GetInt32(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : 0;
    }

    private static long GetInt64(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long result)
            ? result
            : 0;
    }
}
