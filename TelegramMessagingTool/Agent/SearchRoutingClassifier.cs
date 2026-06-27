using System.Text.RegularExpressions;

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
    public static ISearchRoutingClassifier Create(string? mode)
    {
        string normalized = string.IsNullOrWhiteSpace(mode)
            ? "heuristic"
            : mode.Trim().ToLowerInvariant();

        return normalized switch
        {
            "off" => new OffSearchRoutingClassifier(),
            "heuristic" => new HeuristicSearchRoutingClassifier(),
            _ => new HeuristicSearchRoutingClassifier()
        };
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
