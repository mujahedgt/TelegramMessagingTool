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
    string SearchRoutingMode)
{
    public string OllamaChatModel { get; init; } = OllamaModel;

    public string OllamaPlanningModel { get; init; } = OllamaModel;

    public string OllamaDocumentQuestionAnsweringModel { get; init; } = OllamaModel;

    public string OllamaDocumentSummaryModel { get; init; } = OllamaModel;

    public string OllamaToolFinalModel { get; init; } = OllamaModel;

    public string OllamaImageModel { get; init; } = OllamaModel;

    public string OllamaVoiceModel { get; init; } = OllamaModel;
}

public static class BotConfiguration
{
    public const string DefaultEmbeddingModel = "nomic-embed-text";

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
        bool allowPublicAccess = IsEnabled(Environment.GetEnvironmentVariable("ALLOW_PUBLIC_ACCESS"), defaultValue: false);
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
            SearchRoutingMode: NormalizeSearchRoutingMode(Environment.GetEnvironmentVariable("SEARCH_ROUTING_MODE")))
        {
            OllamaChatModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_CHAT"), ollamaModel),
            OllamaPlanningModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_PLAN"), ollamaModel),
            OllamaDocumentQuestionAnsweringModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_DOC_QA"), ollamaModel),
            OllamaDocumentSummaryModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_SUMMARY"), ollamaModel),
            OllamaToolFinalModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_TOOL_FINAL"), ollamaModel),
            OllamaImageModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_IMAGE"), ollamaModel),
            OllamaVoiceModel = NormalizeModelRoute(Environment.GetEnvironmentVariable("OLLAMA_MODEL_VOICE"), ollamaModel)
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
