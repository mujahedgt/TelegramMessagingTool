using System.Text;
using System.Text.Json;

namespace TelegramMessagingTool.Services;

public sealed class OllamaEmbeddingClient : ITextEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly BotSettings _settings;

    public OllamaEmbeddingClient(HttpClient httpClient, BotSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _settings.OllamaEmbeddingModel,
            input = text
        };

        string requestJson = JsonSerializer.Serialize(request);
        using StringContent httpContent = new(requestJson, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.PostAsync(_settings.OllamaEmbeddingUrl, httpContent, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Ollama embedding error: {(int)response.StatusCode} {response.ReasonPhrase}. Make sure `{_settings.OllamaEmbeddingModel}` is pulled locally.");
        }

        if (!TryParseEmbeddingResponse(responseText, out IReadOnlyList<float> embedding) || embedding.Count == 0)
        {
            throw new InvalidOperationException("Ollama returned an invalid embedding response.");
        }

        return embedding;
    }

    public static string BuildEmbedUrl(string chatUrl)
    {
        if (string.IsNullOrWhiteSpace(chatUrl))
        {
            return "http://localhost:11434/api/embed";
        }

        string trimmed = chatUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[..^"/api/chat".Length] + "/api/embed";
        }

        if (trimmed.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[..^"/api/generate".Length] + "/api/embed";
        }

        if (trimmed.EndsWith("/api/embed", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return trimmed + "/api/embed";
    }

    public static bool TryParseEmbeddingResponse(string responseText, out IReadOnlyList<float> embedding)
    {
        embedding = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseText);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("embeddings", out JsonElement embeddings)
                && embeddings.ValueKind == JsonValueKind.Array
                && embeddings.GetArrayLength() > 0)
            {
                JsonElement first = embeddings[0];
                embedding = ParseVector(first);
                return embedding.Count > 0;
            }

            if (root.TryGetProperty("embedding", out JsonElement embeddingElement)
                && embeddingElement.ValueKind == JsonValueKind.Array)
            {
                embedding = ParseVector(embeddingElement);
                return embedding.Count > 0;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<float> ParseVector(JsonElement vectorElement)
    {
        if (vectorElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<float>();
        foreach (JsonElement item in vectorElement.EnumerateArray())
        {
            if (item.TryGetSingle(out float value))
            {
                values.Add(value);
            }
            else if (item.TryGetDouble(out double doubleValue))
            {
                values.Add((float)doubleValue);
            }
        }

        return values;
    }
}
