using TelegramMessagingTool.Tools.GitHub;

namespace TelegramMessagingTool;

public sealed record BotSettings(
    string BotToken,
    string OllamaUrl,
    string OllamaModel,
    string OllamaEmbeddingUrl,
    string OllamaEmbeddingModel,
    bool EnableDocumentEmbeddings,
    bool EnableOnlineSearch,
    long AdminChatId,
    IReadOnlySet<long> AllowedChatIds,
    bool AllowPublicAccess,
    string DatabaseConnectionString,
    bool ApplyMigrations,
    bool LogMessageContent,
    int ConversationMaxHistory,
    string SearchRoutingMode,
    bool EnableSafeCommandTools,
    string SafeCommandProjectRoot,
    bool EnablePlugins,
    string PluginDirectory)
{
    public string OllamaChatModel { get; init; } = OllamaModel;

    public string OllamaPlanningModel { get; init; } = OllamaModel;

    public string OllamaDocumentQuestionAnsweringModel { get; init; } = OllamaModel;

    public string OllamaDocumentSummaryModel { get; init; } = OllamaModel;

    public string OllamaToolFinalModel { get; init; } = OllamaModel;

    public string OllamaImageModel { get; init; } = OllamaModel;

    public string OllamaVoiceModel { get; init; } = OllamaModel;

    public bool EnableImageVision { get; init; }

    public bool EnableAudioTranscription { get; init; }

    public string AudioTranscriptionCommand { get; init; } = string.Empty;

    public string AudioTranscriptionArguments { get; init; } = "{file}";

    public int AudioTranscriptionTimeoutSeconds { get; init; } = 120;

    public bool EnableTextToSpeech { get; init; }

    public bool EnableTelegramTypingIndicator { get; init; }

    public bool EnableStreamingResponses { get; init; }

    public string TextToSpeechCommand { get; init; } = string.Empty;

    public string TextToSpeechArguments { get; init; } = "{text} {output}";

    public int TextToSpeechTimeoutSeconds { get; init; } = 120;

    public string TextToSpeechOutputExtension { get; init; } = ".mp3";

    public bool EnableRepoWriteTools { get; init; }

    public string ImageDescriptionPrompt { get; init; } = BotConfiguration.DefaultImageDescriptionPrompt;

    public GitHubSettings GitHub { get; init; } = GitHubSettings.Disabled;
}

public static class BotConfiguration
{
    public const string DefaultEmbeddingModel = "nomic-embed-text";

    public const string DefaultImageDescriptionPrompt = "Describe this image clearly and concisely. Mention visible text only if you can read it. Do not invent details you cannot see.";

    public static BotSettings LoadFromEnvironment()
    {
        string botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? string.Empty;
        string ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434/api/chat";
        string ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama3.2:3b";
        string ollamaEmbeddingUrl = Environment.GetEnvironmentVariable("OLLAMA_EMBEDDING_URL")
            ?? Services.OllamaEmbeddingClient.BuildEmbedUrl(ollamaUrl);
        string ollamaEmbeddingModel = NormalizeEmbeddingModel(Environment.GetEnvironmentVariable("OLLAMA_EMBEDDING_MODEL"));
        bool enableDocumentEmbeddings = IsEnabled(Environment.GetEnvironmentVariable("ENABLE_DOCUMENT_EMBEDDINGS"), defaultValue: false);
        bool enableOnlineSearch = IsEnabled(Environment.GetEnvironmentVariable("ENABLE_ONLINE_SEARCH"), defaultValue: false);
        bool enableSafeCommandTools = IsEnabled(Environment.GetEnvironmentVariable("ENABLE_SAFE_COMMAND_TOOLS"), defaultValue: false);
        bool enablePlugins = IsEnabled(Environment.GetEnvironmentVariable("ENABLE_PLUGINS"), defaultValue: false);
        bool allowPublicAccess = IsEnabled(Environment.GetEnvironmentVariable("ALLOW_PUBLIC_ACCESS"), defaultValue: false);
        string safeCommandProjectRoot = NormalizeFullPath(Environment.GetEnvironmentVariable("SAFE_COMMAND_PROJECT_ROOT"), Environment.CurrentDirectory);
        string pluginDirectory = NormalizeFullPath(Environment.GetEnvironmentVariable("PLUGIN_DIRECTORY"), Path.Combine(Environment.CurrentDirectory, "plugins"));
        string databaseConnectionString = Environment.GetEnvironmentVariable("TELEGRAM_DB_CONNECTION")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=TelegramMessagingTool;Trusted_Connection=True;TrustServerCertificate=True";

        long.TryParse(Environment.GetEnvironmentVariable("ADMIN_CHAT_ID"), out long adminChatId);

        return new BotSettings(
            BotToken: botToken,
            OllamaUrl: ollamaUrl,
            OllamaModel: ollamaModel,
            OllamaEmbeddingUrl: ollamaEmbeddingUrl,
            OllamaEmbeddingModel: ollamaEmbeddingModel,
            EnableDocumentEmbeddings: enableDocumentEmbeddings,
            EnableOnlineSearch: enableOnlineSearch,
            AdminChatId: adminChatId,
            AllowedChatIds: BotAccessPolicy.ParseAllowedChatIds(Environment.GetEnvironmentVariable("ALLOWED_CHAT_IDS")),
            AllowPublicAccess: allowPublicAccess,
            DatabaseConnectionString: databaseConnectionString,
            ApplyMigrations: IsEnabled(Environment.GetEnvironmentVariable("APPLY_MIGRATIONS"), defaultValue: true),
            LogMessageContent: IsEnabled(Environment.GetEnvironmentVariable("LOG_MESSAGE_CONTENT"), defaultValue: false),
            ConversationMaxHistory: ParseClampedInt(Environment.GetEnvironmentVariable("CONVERSATION_MAX_HISTORY"), defaultValue: 8, minValue: 1, maxValue: 50),
            SearchRoutingMode: NormalizeSearchRoutingMode(Environment.GetEnvironmentVariable("SEARCH_ROUTING_MODE")),
            EnableSafeCommandTools: enableSafeCommandTools,
            SafeCommandProjectRoot: safeCommandProjectRoot,
            EnablePlugins: enablePlugins,
            PluginDirectory: pluginDirectory)
        {
            OllamaChatModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_CHAT"), ollamaModel),
            OllamaPlanningModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_PLAN"), ollamaModel),
            OllamaDocumentQuestionAnsweringModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_DOC_QA"), ollamaModel),
            OllamaDocumentSummaryModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_SUMMARY"), ollamaModel),
            OllamaToolFinalModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_TOOL_FINAL"), ollamaModel),
            OllamaImageModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_IMAGE"), ollamaModel),
            OllamaVoiceModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_VOICE"), ollamaModel),
            EnableImageVision = IsEnabled(Environment.GetEnvironmentVariable("ENABLE_IMAGE_VISION"), defaultValue: false),
            EnableAudioTranscription = IsEnabled(Environment.GetEnvironmentVariable("ENABLE_AUDIO_TRANSCRIPTION"), defaultValue: false),
            AudioTranscriptionCommand = NormalizeOptionalText(Environment.GetEnvironmentVariable("AUDIO_TRANSCRIPTION_COMMAND")),
            AudioTranscriptionArguments = NormalizeAudioTranscriptionArguments(Environment.GetEnvironmentVariable("AUDIO_TRANSCRIPTION_ARGUMENTS")),
            AudioTranscriptionTimeoutSeconds = ParseClampedInt(Environment.GetEnvironmentVariable("AUDIO_TRANSCRIPTION_TIMEOUT_SECONDS"), defaultValue: 120, minValue: 5, maxValue: 300),
            EnableTextToSpeech = IsEnabled(Environment.GetEnvironmentVariable("ENABLE_TEXT_TO_SPEECH"), defaultValue: false),
            EnableTelegramTypingIndicator = IsEnabled(Environment.GetEnvironmentVariable("ENABLE_TELEGRAM_TYPING_INDICATOR"), defaultValue: false),
            EnableStreamingResponses = IsEnabled(Environment.GetEnvironmentVariable("ENABLE_STREAMING_RESPONSES"), defaultValue: false),
            TextToSpeechCommand = NormalizeOptionalText(Environment.GetEnvironmentVariable("TEXT_TO_SPEECH_COMMAND")),
            TextToSpeechArguments = NormalizeTextToSpeechArguments(Environment.GetEnvironmentVariable("TEXT_TO_SPEECH_ARGUMENTS")),
            TextToSpeechTimeoutSeconds = ParseClampedInt(Environment.GetEnvironmentVariable("TEXT_TO_SPEECH_TIMEOUT_SECONDS"), defaultValue: 120, minValue: 5, maxValue: 300),
            TextToSpeechOutputExtension = NormalizeTextToSpeechOutputExtension(Environment.GetEnvironmentVariable("TEXT_TO_SPEECH_OUTPUT_EXTENSION")),
            EnableRepoWriteTools = IsEnabled(Environment.GetEnvironmentVariable("ENABLE_REPO_WRITE_TOOLS"), defaultValue: false),
            ImageDescriptionPrompt = NormalizeImageDescriptionPrompt(Environment.GetEnvironmentVariable("IMAGE_DESCRIPTION_PROMPT")),
            GitHub = GitHubSettings.LoadFromEnvironment()
        };
    }

    public static string NormalizeEmbeddingModel(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DefaultEmbeddingModel
            : value.Trim();
    }

    public static string NormalizeModelRoute(string? value, string fallbackModel)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallbackModel.Trim()
            : value.Trim();
    }

    public static string NormalizeImageDescriptionPrompt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultImageDescriptionPrompt;
        }

        string normalized = value.Trim();
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }

    public static string NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    public static string NormalizeAudioTranscriptionArguments(string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "{file}" : value.Trim();
        return normalized.Contains("{file}", StringComparison.OrdinalIgnoreCase) ? normalized : normalized + " {file}";
    }

    public static string NormalizeTextToSpeechArguments(string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "{text} {output}" : value.Trim();
        if (!normalized.Contains("{text}", StringComparison.OrdinalIgnoreCase))
        {
            normalized += " {text}";
        }

        if (!normalized.Contains("{output}", StringComparison.OrdinalIgnoreCase))
        {
            normalized += " {output}";
        }

        return normalized;
    }

    public static string NormalizeTextToSpeechOutputExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ".mp3";
        }

        string normalized = value.Trim().TrimStart('.').ToLowerInvariant();
        return normalized is "mp3" or "wav" or "m4a" or "ogg" or "oga" or "opus" or "flac"
            ? "." + normalized
            : ".mp3";
    }

    public static string NormalizeSearchRoutingMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "heuristic";
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized is "heuristic" or "off" or "llm"
            ? normalized
            : "heuristic";
    }

    public static string NormalizeFullPath(string? value, string fallbackPath)
    {
        string path = string.IsNullOrWhiteSpace(value) ? fallbackPath : value.Trim();
        return Path.GetFullPath(path);
    }

    public static int ParseClampedInt(string? value, int defaultValue, int minValue, int maxValue)
    {
        if (minValue > maxValue)
        {
            throw new ArgumentException("Minimum value cannot be greater than maximum value.", nameof(minValue));
        }

        int parsedValue = int.TryParse(value, out int parsed) ? parsed : defaultValue;
        return Math.Clamp(parsedValue, minValue, maxValue);
    }

    public static bool IsEnabled(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

public static class BotAccessPolicy
{
    public static IReadOnlySet<long> ParseAllowedChatIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<long>();
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => long.TryParse(x, out long value) ? value : (long?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();
    }

    public static bool IsAllowed(
        long chatId,
        IReadOnlySet<long> allowedChatIds,
        long adminChatId,
        bool allowPublicAccess)
    {
        if (IsAdmin(chatId, adminChatId))
        {
            return true;
        }

        if (allowedChatIds.Contains(chatId))
        {
            return true;
        }

        return allowPublicAccess && allowedChatIds.Count == 0;
    }

    public static string AccessDeniedMessage(bool allowPublicAccess, IReadOnlySet<long> allowedChatIds, long adminChatId)
    {
        if (!allowPublicAccess && allowedChatIds.Count == 0 && adminChatId <= 0)
        {
            return "Access denied. The bot is locked because ALLOWED_CHAT_IDS is empty, ADMIN_CHAT_ID is not configured, and ALLOW_PUBLIC_ACCESS is not enabled.";
        }

        return "Access denied. Ask the bot administrator to add your chat ID.";
    }

    public static string DescribeAccessMode(IReadOnlySet<long> allowedChatIds, long adminChatId, bool allowPublicAccess)
    {
        if (allowPublicAccess && allowedChatIds.Count == 0)
        {
            return "public override";
        }

        if (allowedChatIds.Count > 0)
        {
            return "allowlist";
        }

        if (adminChatId > 0)
        {
            return "admin-only";
        }

        return "locked";
    }

    public static bool IsAdmin(long chatId, long adminChatId)
    {
        return adminChatId > 0 && chatId == adminChatId;
    }

    public static string AdminOnlyMessage(long adminChatId)
    {
        return adminChatId <= 0
            ? "This command is admin-only, but ADMIN_CHAT_ID is not configured. Set ADMIN_CHAT_ID before using risky local-control commands."
            : "This command is admin-only. Only the configured ADMIN_CHAT_ID can use risky local-control commands.";
    }
}

public static class TelegramMessageFormatter
{
    public const int TelegramMessageLimit = 4096;

    public static IEnumerable<string> SplitForTelegram(string text, int maxLength = TelegramMessageLimit)
    {
        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Maximum length must be positive.");
        }

        if (string.IsNullOrEmpty(text))
        {
            yield return string.Empty;
            yield break;
        }

        for (int start = 0; start < text.Length; start += maxLength)
        {
            int length = Math.Min(maxLength, text.Length - start);
            yield return text.Substring(start, length);
        }
    }

    public static string RedactForLogs(string? text, int maxLength = 500)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string sanitized = text
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        return sanitized.Length <= maxLength
            ? sanitized
            : sanitized[..maxLength] + "... [truncated]";
    }
}
