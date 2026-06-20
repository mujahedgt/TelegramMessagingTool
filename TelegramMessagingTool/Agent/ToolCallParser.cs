using System.Text.Json;

namespace TelegramMessagingTool.Agent;

public sealed record ToolCallParseResult(
    bool IsToolCall,
    string? ToolName,
    string Input,
    string? Error)
{
    public static ToolCallParseResult NotToolCall() => new(false, null, string.Empty, null);

    public static ToolCallParseResult ToolCall(string toolName, string input) => new(true, toolName, input, null);

    public static ToolCallParseResult Invalid(string error) => new(false, null, string.Empty, error);
}

public static class ToolCallParser
{
    public static ToolCallParseResult Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ToolCallParseResult.NotToolCall();
        }

        string trimmed = text.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return ToolCallParseResult.NotToolCall();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("type", out JsonElement typeElement)
                || !string.Equals(typeElement.GetString(), "tool_call", StringComparison.OrdinalIgnoreCase))
            {
                return ToolCallParseResult.NotToolCall();
            }

            if (!root.TryGetProperty("tool", out JsonElement toolElement))
            {
                return ToolCallParseResult.Invalid("Tool call JSON is missing the 'tool' property.");
            }

            string? toolName = toolElement.GetString();
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return ToolCallParseResult.Invalid("Tool call JSON has an empty tool name.");
            }

            string input = root.TryGetProperty("input", out JsonElement inputElement)
                ? inputElement.GetString() ?? string.Empty
                : string.Empty;

            return ToolCallParseResult.ToolCall(toolName.Trim(), input);
        }
        catch (JsonException)
        {
            return ToolCallParseResult.NotToolCall();
        }
    }
}
