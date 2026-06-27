using System.Text.Json;
using System.Text.RegularExpressions;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Agent;

public interface ISearchRoutingClassifier
{
    Task<SearchRoutingDecision> ClassifyAsync(
        IReadOnlyList<OllamaMessageDto> conversationContext,
        CancellationToken cancellationToken);
}

public sealed record SearchRoutingDecision(
    bool ShouldSearch,
    string Query,
    string Reason,
    double Confidence)
{
    public static SearchRoutingDecision NoSearch(string reason) => new(false, string.Empty, reason, 0);
}

public sealed class OffSearchRoutingClassifier : ISearchRoutingClassifier
{
    public Task<SearchRoutingDecision> ClassifyAsync(
        IReadOnlyList<OllamaMessageDto> conversationContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SearchRoutingDecision.NoSearch("Direct search routing is disabled by SEARCH_ROUTING_MODE=off."));
    }
}

public static class SearchRoutingClassifierFactory
{
    public static ISearchRoutingClassifier Create(string? mode, IChatClient? chatClient = null)
    {
        string normalized = string.IsNullOrWhiteSpace(mode)
            ? "heuristic"
            : mode.Trim().ToLowerInvariant();

        return normalized switch
        {
            "off" => new OffSearchRoutingClassifier(),
            "llm" when chatClient is not null => new LlmSearchRoutingClassifier(chatClient),
            "heuristic" => new HeuristicSearchRoutingClassifier(),
            _ => new HeuristicSearchRoutingClassifier()
        };
    }
}

public sealed class LlmSearchRoutingClassifier : ISearchRoutingClassifier
{
    private readonly IChatClient _chatClient;

    public LlmSearchRoutingClassifier(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<SearchRoutingDecision> ClassifyAsync(
        IReadOnlyList<OllamaMessageDto> conversationContext,
        CancellationToken cancellationToken)
    {
        string latestUserMessage = conversationContext
            .LastOrDefault(x => string.Equals(x.role, "user", StringComparison.OrdinalIgnoreCase))
            ?.content
            ?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(latestUserMessage))
        {
            return SearchRoutingDecision.NoSearch("No user message found.");
        }

        var classifierContext = new List<OllamaMessageDto>
        {
            new("system", BuildClassifierPrompt()),
            new("user", latestUserMessage)
        };

        string response = await _chatClient.AskAsync(classifierContext, cancellationToken, ModelTaskKind.Chat);
        return TryParseDecision(response, out SearchRoutingDecision decision)
            ? decision
            : SearchRoutingDecision.NoSearch("LLM search-routing classifier returned invalid JSON.");
    }

    public static string BuildClassifierPrompt()
    {
        return """
You decide whether the latest user message needs live web search before answering.
Return ONLY strict JSON with this exact shape:
{"should_search":false,"query":"","reason":"brief reason","confidence":0.0}

Rules:
- should_search=true only if the answer needs current/external public facts.
- Search for latest/current/recent versions, prices, market values, news, releases, product specs, or anything likely stale.
- should_search=false for coding explanations, local project questions, database/schema questions, personal conversation, or definitions that do not need current facts.
- If should_search=true, query must be a clean search query.
- confidence must be between 0 and 1.
""";
    }

    public static bool TryParseDecision(string response, out SearchRoutingDecision decision)
    {
        decision = SearchRoutingDecision.NoSearch("Invalid search-routing JSON.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(response.Trim());
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("should_search", out JsonElement shouldSearchElement)
                || shouldSearchElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                return false;
            }

            bool shouldSearch = shouldSearchElement.GetBoolean();
            string query = root.TryGetProperty("query", out JsonElement queryElement) && queryElement.ValueKind == JsonValueKind.String
                ? queryElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            string reason = root.TryGetProperty("reason", out JsonElement reasonElement) && reasonElement.ValueKind == JsonValueKind.String
                ? reasonElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            double confidence = root.TryGetProperty("confidence", out JsonElement confidenceElement) && confidenceElement.TryGetDouble(out double parsedConfidence)
                ? Math.Clamp(parsedConfidence, 0, 1)
                : 0;

            if (shouldSearch && string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            decision = new SearchRoutingDecision(
                shouldSearch,
                shouldSearch ? query : string.Empty,
                string.IsNullOrWhiteSpace(reason) ? "LLM search-routing decision." : reason,
                confidence);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed class HeuristicSearchRoutingClassifier : ISearchRoutingClassifier
{
    public Task<SearchRoutingDecision> ClassifyAsync(
        IReadOnlyList<OllamaMessageDto> conversationContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Classify(conversationContext));
    }

    public SearchRoutingDecision Classify(IReadOnlyList<OllamaMessageDto> conversationContext)
    {
        string? userText = conversationContext
            .LastOrDefault(x => string.Equals(x.role, "user", StringComparison.OrdinalIgnoreCase))
            ?.content;

        if (string.IsNullOrWhiteSpace(userText))
        {
            return SearchRoutingDecision.NoSearch("No user message found.");
        }

        bool asksForSearch = ContainsAny(userText, ["search", "searsh", "google", "look up", "find online", "online", "web"]);
        bool asksForCurrentInfo = ContainsAny(userText, ["latest", "newest", "recent", "current", "currently", "today", "this year", "new model", "new car", "just released", "released", "upcoming", "2025", "2026"]);
        bool asksForCurrentMarketData = ContainsAny(userText, ["price", "prices", "market value", "current value", "for sale"]);
        if (!asksForSearch && !asksForCurrentInfo && !asksForCurrentMarketData)
        {
            return SearchRoutingDecision.NoSearch("Message does not request search, current information, or current market data.");
        }

        Match quoted = Regex.Match(userText, "[\"“”'](?<query>[^\"“”']{2,120})[\"“”']");
        string query = quoted.Success ? quoted.Groups["query"].Value.Trim() : userText.Trim();

        if (asksForCurrentMarketData && !ContainsAny(query, ["price", "prices", "market", "value", "sale", "spec", "specs"]))
        {
            query += " price specs";
        }

        if (asksForCurrentInfo && !ContainsAny(query, ["official", "2025", "2026", "latest model", "new model"]))
        {
            query += " 2026 official latest model";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return SearchRoutingDecision.NoSearch("Search query was empty after normalization.");
        }

        string reason = asksForSearch
            ? "User explicitly requested online search."
            : asksForCurrentInfo
                ? "User asked for current or recent information."
                : "User asked for current market data.";
        return new SearchRoutingDecision(true, query, reason, 0.75);
    }

    public static bool TryBuildDirectSearchQuery(IReadOnlyList<OllamaMessageDto> conversationContext, out string query)
    {
        SearchRoutingDecision decision = new HeuristicSearchRoutingClassifier().Classify(conversationContext);
        query = decision.Query;
        return decision.ShouldSearch;
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
