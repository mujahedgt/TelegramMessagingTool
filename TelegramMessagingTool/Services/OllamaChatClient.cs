using System.Text;
using System.Text.Json;

namespace TelegramMessagingTool.Services;

public sealed class OllamaChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly BotSettings _settings;

    public OllamaChatClient(HttpClient httpClient, BotSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<string> AskAsync(
        List<OllamaMessageDto> conversationContext,
        CancellationToken cancellationToken)
    {
        var ollamaRequest = new
        {
            model = _settings.OllamaModel,
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
}
