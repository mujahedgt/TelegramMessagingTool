using System.Text.Json;
using System.Text.RegularExpressions;

namespace TelegramMessagingTool.Plugins;

public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string EntryAssembly,
    bool Enabled,
    string RiskLevel,
    bool IsReadOnly,
    string SafetySummary,
    IReadOnlyList<string> AllowedToolNames)
{
    private static readonly Regex ToolNameRegex = new("^[a-z][a-z0-9_]{1,40}$", RegexOptions.Compiled);

    public static PluginManifestParseResult TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return PluginManifestParseResult.Fail("Plugin manifest JSON is empty.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return PluginManifestParseResult.Fail("Plugin manifest root must be a JSON object.");
            }

            string? id = ReadRequiredString(root, "id");
            string? name = ReadRequiredString(root, "name");
            string? version = ReadRequiredString(root, "version");
            string? entryAssembly = ReadRequiredString(root, "entryAssembly");
            if (id is null || name is null || version is null || entryAssembly is null)
            {
                return PluginManifestParseResult.Fail("Plugin manifest must include id, name, version, and entryAssembly string properties.");
            }

            bool enabled = ReadOptionalBool(root, "enabled", defaultValue: false);
            string riskLevel = ReadOptionalString(root, "riskLevel", "medium").ToLowerInvariant();
            if (riskLevel is not ("low" or "medium" or "high"))
            {
                return PluginManifestParseResult.Fail("Plugin manifest riskLevel must be low, medium, or high.");
            }

            bool isReadOnly = ReadOptionalBool(root, "isReadOnly", defaultValue: riskLevel == "low");
            string safetySummary = ReadOptionalString(root, "safetySummary", "No plugin safety summary provided.");
            if (safetySummary.Length > 240)
            {
                safetySummary = safetySummary[..240];
            }

            if (!root.TryGetProperty("allowedToolNames", out JsonElement toolsElement) || toolsElement.ValueKind != JsonValueKind.Array)
            {
                return PluginManifestParseResult.Fail("Plugin manifest must include allowedToolNames array.");
            }

            var toolNames = new List<string>();
            foreach (JsonElement element in toolsElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return PluginManifestParseResult.Fail("Every allowedToolNames entry must be a string.");
                }

                string toolName = element.GetString()?.Trim() ?? string.Empty;
                if (!IsValidToolName(toolName))
                {
                    return PluginManifestParseResult.Fail($"Invalid plugin tool name '{toolName}'. Tool names must match ^[a-z][a-z0-9_]{{1,40}}$.");
                }

                if (toolNames.Contains(toolName, StringComparer.OrdinalIgnoreCase))
                {
                    return PluginManifestParseResult.Fail($"Duplicate tool name '{toolName}' inside plugin manifest.");
                }

                toolNames.Add(toolName);
            }

            if (toolNames.Count == 0)
            {
                return PluginManifestParseResult.Fail("Plugin manifest must allow at least one tool name.");
            }

            return PluginManifestParseResult.Ok(new PluginManifest(
                Id: id,
                Name: name,
                Version: version,
                EntryAssembly: entryAssembly,
                Enabled: enabled,
                RiskLevel: riskLevel,
                IsReadOnly: isReadOnly,
                SafetySummary: safetySummary,
                AllowedToolNames: toolNames));
        }
        catch (JsonException ex)
        {
            return PluginManifestParseResult.Fail($"Plugin manifest JSON is invalid: {ex.Message}");
        }
    }

    public static bool IsValidToolName(string toolName)
    {
        return !string.IsNullOrWhiteSpace(toolName) && ToolNameRegex.IsMatch(toolName.Trim());
    }

    private static string? ReadRequiredString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    }

    private static string ReadOptionalString(JsonElement root, string propertyName, string defaultValue)
    {
        return root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : defaultValue;
    }

    private static bool ReadOptionalBool(JsonElement root, string propertyName, bool defaultValue)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }
}

public sealed record PluginManifestParseResult(bool Success, PluginManifest? Manifest, string Error)
{
    public static PluginManifestParseResult Ok(PluginManifest manifest) => new(true, manifest, string.Empty);

    public static PluginManifestParseResult Fail(string error) => new(false, null, error);
}
