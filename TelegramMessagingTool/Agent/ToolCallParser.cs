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

        ToolCallParseResult fullTextResult = TryParseJsonToolCall(trimmed);
        if (fullTextResult.IsToolCall || fullTextResult.Error is not null)
        {
            return fullTextResult;
        }

        foreach (string candidate in ExtractJsonObjectCandidates(trimmed))
        {
            ToolCallParseResult candidateResult = TryParseJsonToolCall(candidate);
            if (candidateResult.IsToolCall || candidateResult.Error is not null)
            {
                return candidateResult;
            }
        }

        return ToolCallParseResult.NotToolCall();
    }

    private static ToolCallParseResult TryParseJsonToolCall(string json)
    {
        if (!json.TrimStart().StartsWith('{') || !json.TrimEnd().EndsWith('}'))
        {
            return ToolCallParseResult.NotToolCall();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
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

    private static IEnumerable<string> ExtractJsonObjectCandidates(string text)
    {
        for (int start = 0; start < text.Length; start++)
        {
            if (text[start] != '{')
            {
                continue;
            }

            bool inString = false;
            bool escaped = false;
            int depth = 0;

            for (int index = start; index < text.Length; index++)
            {
                char current = text[index];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (current == '{')
                {
                    depth++;
                }
                else if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return text[start..(index + 1)];
                        break;
                    }
                }
            }
        }
    }
}
