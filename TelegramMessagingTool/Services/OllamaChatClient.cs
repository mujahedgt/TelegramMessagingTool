using System.Text;
using System.Text.Json;

namespace TelegramMessagingTool.Services;

public sealed class OllamaChatClient : IChatClient, IStreamingChatClient
{
    private readonly HttpClient _httpClient;
    private readonly BotSettings _settings;
    private readonly ModelRoutingService _modelRoutingService;

    public OllamaChatClient(HttpClient httpClient, BotSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _modelRoutingService = new ModelRoutingService(settings);
    }

    public async Task<string> AskAsync(
        List<OllamaMessageDto> conversationContext,
        CancellationToken cancellationToken,
        ModelTaskKind taskKind = ModelTaskKind.Chat)
    {
        var ollamaRequest = new
        {
            model = _modelRoutingService.GetModel(taskKind),
            stream = false,
            options = new
            {
                temperature = 0.2
            },
            messages = conversationContext
        };

        string requestJson = JsonSerializer.Serialize(ollamaRequest);

        using StringContent httpContent = new(
            requestJson,
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await _httpClient.PostAsync(
            _settings.OllamaUrl,
            httpContent,
            cancellationToken);

        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return $"Ollama returned an error: {(int)response.StatusCode} {response.ReasonPhrase}";
        }

        return ParseAssistantContent(responseText);
    }

    public async Task<string> AskStreamingAsync(
        List<OllamaMessageDto> conversationContext,
        Func<string, CancellationToken, Task>? onDeltaAsync,
        CancellationToken cancellationToken,
        ModelTaskKind taskKind = ModelTaskKind.Chat)
    {
        var ollamaRequest = new
        {
            model = _modelRoutingService.GetModel(taskKind),
            stream = true,
            options = new
            {
                temperature = 0.2
            },
            messages = conversationContext
        };

        string requestJson = JsonSerializer.Serialize(ollamaRequest);
        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.OllamaUrl)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return $"Ollama returned an error: {(int)response.StatusCode} {response.ReasonPhrase}";
        }

        var answer = new StringBuilder();
        bool sawValidChunk = false;
        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            StreamingChunkParseResult chunk = ParseStreamingChunk(line);
            if (!chunk.IsValid)
            {
                return "Invalid response received from Ollama.";
            }

            sawValidChunk = true;
            if (!string.IsNullOrEmpty(chunk.ContentDelta))
            {
                answer.Append(chunk.ContentDelta);
                if (onDeltaAsync is not null)
                {
                    await onDeltaAsync(chunk.ContentDelta, cancellationToken);
                }
            }

            if (chunk.Done)
            {
                break;
            }
        }

        if (!sawValidChunk)
        {
            return "Invalid response received from Ollama.";
        }

        string content = answer.ToString().Trim();
        return string.IsNullOrWhiteSpace(content)
            ? "Empty response from Ollama."
            : content;
    }

    public static string ParseAssistantContent(string responseText)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseText);

            string? content = document
                .RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content)
                ? "Empty response from Ollama."
                : content.Trim();
        }
        catch (JsonException)
        {
            return "Invalid response received from Ollama.";
        }
        catch (KeyNotFoundException)
        {
            return "Invalid response received from Ollama.";
        }
        catch (InvalidOperationException)
        {
            return "Invalid response received from Ollama.";
        }
    }

    public static string ParseStreamingAssistantContent(string responseText)
    {
        var answer = new StringBuilder();
        bool sawValidChunk = false;

        foreach (string line in responseText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            StreamingChunkParseResult chunk = ParseStreamingChunk(line);
            if (!chunk.IsValid)
            {
                return "Invalid response received from Ollama.";
            }

            sawValidChunk = true;
            if (!string.IsNullOrEmpty(chunk.ContentDelta))
            {
                answer.Append(chunk.ContentDelta);
            }

            if (chunk.Done)
            {
                break;
            }
        }

        if (!sawValidChunk)
        {
            return "Invalid response received from Ollama.";
        }

        string content = answer.ToString().Trim();
        return string.IsNullOrWhiteSpace(content)
            ? "Empty response from Ollama."
            : content;
    }

    private static StreamingChunkParseResult ParseStreamingChunk(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            bool done = root.TryGetProperty("done", out JsonElement doneElement)
                && doneElement.ValueKind == JsonValueKind.True;

            string content = string.Empty;
            if (root.TryGetProperty("message", out JsonElement messageElement)
                && messageElement.ValueKind == JsonValueKind.Object
                && messageElement.TryGetProperty("content", out JsonElement contentElement)
                && contentElement.ValueKind == JsonValueKind.String)
            {
                content = contentElement.GetString() ?? string.Empty;
            }

            return new StreamingChunkParseResult(true, content, done);
        }
        catch (JsonException)
        {
            return StreamingChunkParseResult.Invalid;
        }
        catch (InvalidOperationException)
        {
            return StreamingChunkParseResult.Invalid;
        }
    }

    private sealed record StreamingChunkParseResult(bool IsValid, string ContentDelta, bool Done)
    {
        public static StreamingChunkParseResult Invalid { get; } = new(false, string.Empty, false);
    }
}
