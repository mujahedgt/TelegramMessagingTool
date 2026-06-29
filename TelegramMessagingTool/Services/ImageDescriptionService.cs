using System.Text;
using System.Text.Json;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public interface IImageDescriptionService
{
    Task<ImageDescriptionResult> DescribeAsync(
        UploadedFile imageFile,
        string prompt,
        CancellationToken cancellationToken);
}

public sealed record ImageDescriptionResult(bool Success, string Output)
{
    public static ImageDescriptionResult Ok(string output) => new(true, output);

    public static ImageDescriptionResult Failed(string output) => new(false, output);
}

public sealed class OllamaImageDescriptionService : IImageDescriptionService
{
    private const long MaxImageBytes = 10L * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly BotSettings _settings;
    private readonly ModelRoutingService _modelRoutingService;

    public OllamaImageDescriptionService(HttpClient httpClient, BotSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _modelRoutingService = new ModelRoutingService(settings);
    }

    public async Task<ImageDescriptionResult> DescribeAsync(
        UploadedFile imageFile,
        string prompt,
        CancellationToken cancellationToken)
    {
        string absolutePath = Path.GetFullPath(imageFile.AbsolutePath);
        if (!File.Exists(absolutePath))
        {
            return ImageDescriptionResult.Failed("Image file is missing on disk.");
        }

        FileInfo fileInfo = new(absolutePath);
        if (fileInfo.Length > MaxImageBytes)
        {
            return ImageDescriptionResult.Failed($"Image is too large for inline vision analysis. Maximum supported size is {MaxImageBytes} bytes.");
        }

        string imageBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(absolutePath, cancellationToken));
        var request = new
        {
            model = _modelRoutingService.GetModel(ModelTaskKind.Image),
            stream = false,
            options = new
            {
                temperature = 0.1
            },
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt,
                    images = new[] { imageBase64 }
                }
            }
        };

        string requestJson = JsonSerializer.Serialize(request);
        using StringContent httpContent = new(requestJson, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.PostAsync(_settings.OllamaUrl, httpContent, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return ImageDescriptionResult.Failed($"Ollama image route returned an error: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        string description = OllamaChatClient.ParseAssistantContent(responseText);
        return description.StartsWith("Invalid response", StringComparison.OrdinalIgnoreCase)
            || description.StartsWith("Empty response", StringComparison.OrdinalIgnoreCase)
            ? ImageDescriptionResult.Failed(description)
            : ImageDescriptionResult.Ok(description);
    }
}
