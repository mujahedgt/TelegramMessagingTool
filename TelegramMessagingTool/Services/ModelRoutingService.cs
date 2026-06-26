namespace TelegramMessagingTool.Services;

public enum ModelTaskKind
{
    Chat,
    Planning,
    DocumentQuestionAnswering,
    DocumentSummary,
    ToolFinalAnswer,
    Image,
    Voice
}

public sealed class ModelRoutingService
{
    private readonly BotSettings _settings;

    public ModelRoutingService(BotSettings settings)
    {
        _settings = settings;
    }

    public string GetModel(ModelTaskKind taskKind)
    {
        return taskKind switch
        {
            ModelTaskKind.Chat => RouteOrDefault(_settings.OllamaChatModel),
            ModelTaskKind.Planning => RouteOrDefault(_settings.OllamaPlanningModel),
            ModelTaskKind.DocumentQuestionAnswering => RouteOrDefault(_settings.OllamaDocumentQuestionAnsweringModel),
            ModelTaskKind.DocumentSummary => RouteOrDefault(_settings.OllamaDocumentSummaryModel),
            ModelTaskKind.ToolFinalAnswer => RouteOrDefault(_settings.OllamaToolFinalModel),
            ModelTaskKind.Image => RouteOrDefault(_settings.OllamaImageModel),
            ModelTaskKind.Voice => RouteOrDefault(_settings.OllamaVoiceModel),
            _ => _settings.OllamaModel
        };
    }

    public string RenderSummary()
    {
        return string.Join(", ",
        [
            $"chat={GetModel(ModelTaskKind.Chat)}",
            $"plan={GetModel(ModelTaskKind.Planning)}",
            $"doc_qa={GetModel(ModelTaskKind.DocumentQuestionAnswering)}",
            $"summary={GetModel(ModelTaskKind.DocumentSummary)}",
            $"tool_final={GetModel(ModelTaskKind.ToolFinalAnswer)}",
            $"image={GetModel(ModelTaskKind.Image)}",
            $"voice={GetModel(ModelTaskKind.Voice)}",
            $"embedding={_settings.OllamaEmbeddingModel}"
        ]);
    }

    private string RouteOrDefault(string? routeModel)
    {
        return string.IsNullOrWhiteSpace(routeModel)
            ? _settings.OllamaModel
            : routeModel.Trim();
    }
}
