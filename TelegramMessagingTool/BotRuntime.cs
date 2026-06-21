namespace TelegramMessagingTool;

public sealed record BotSettings(
    string BotToken,
    string OllamaUrl,
    string OllamaModel,
    string OllamaEmbeddingUrl,
    string OllamaEmbeddingModel,
    bool EnableDocumentEmbeddings,
    long AdminChatId,
    IReadOnlySet<long> AllowedChatIds,
    string DatabaseConnectionString,
    bool ApplyMigrations,
    bool LogMessageContent);

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
            AdminChatId: adminChatId,
            AllowedChatIds: BotAccessPolicy.ParseAllowedChatIds(Environment.GetEnvironmentVariable("ALLOWED_CHAT_IDS")),
            DatabaseConnectionString: databaseConnectionString,
            ApplyMigrations: IsEnabled(Environment.GetEnvironmentVariable("APPLY_MIGRATIONS"), defaultValue: true),
            LogMessageContent: IsEnabled(Environment.GetEnvironmentVariable("LOG_MESSAGE_CONTENT"), defaultValue: false));
    }

    public static string NormalizeEmbeddingModel(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DefaultEmbeddingModel
            : value.Trim();
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

    public static bool IsAllowed(long chatId, IReadOnlySet<long> allowedChatIds)
    {
        return allowedChatIds.Count == 0 || allowedChatIds.Contains(chatId);
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
