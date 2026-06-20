using System.Net;
using System.Text.RegularExpressions;

namespace TelegramMessagingTool.Tools;

public sealed partial class OnlineSearchTool : IAgentTool
{
    private readonly HttpClient _httpClient;

    public OnlineSearchTool(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string Name => "online_search";

    public string Description => "Searches the web for current public information using public HTML search endpoints.";

    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        string query = input.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Fail("Usage: provide a search query.");
        }

        if (query.Length > 200)
        {
            query = query[..200];
        }

        var errors = new List<string>();
        foreach (string searchQuery in BuildSearchQueryVariants(query))
        {
            foreach (Uri uri in BuildSearchUris(searchQuery))
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    ApplyBrowserLikeHeaders(request);

                    using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                    string html = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        errors.Add($"{uri.Host}: {(int)response.StatusCode} {response.ReasonPhrase}");
                        continue;
                    }

                    if (LooksLikeBotChallenge(html))
                    {
                        errors.Add($"{uri.Host}: bot challenge page returned");
                        continue;
                    }

                    IReadOnlyList<SearchResult> results = ParseSearchHtml(html).Take(6).ToList();
                    if (results.Count == 0)
                    {
                        errors.Add($"{uri.Host} [{searchQuery}]: no parsable results");
                        continue;
                    }

                    IReadOnlyList<PageExtract> pageExtracts = await ReadTopResultPagesAsync(results, cancellationToken);
                    return ToolResult.Ok(RenderResults(query, searchQuery, results, uri.Host, pageExtracts));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    errors.Add($"{uri.Host} [{searchQuery}]: {ex.Message}");
                }
            }
        }


        return ToolResult.Fail("Search failed after trying fallback providers: " + string.Join(" | ", errors));
    }

    public static Uri BuildSearchUri(string query)
    {
        string encoded = Uri.EscapeDataString(query.Trim());
        return new Uri($"https://www.startpage.com/sp/search?query={encoded}");
    }

    public static IReadOnlyList<Uri> BuildSearchUris(string query)
    {
        string encoded = Uri.EscapeDataString(query.Trim());
        return [
            new Uri($"https://lite.duckduckgo.com/lite/?q={encoded}"),
            new Uri($"https://www.startpage.com/sp/search?query={encoded}"),
            new Uri($"https://www.mojeek.com/search?q={encoded}")
        ];
    }

    public static IReadOnlyList<string> BuildSearchQueryVariants(string query)
    {
        string trimmed = WhitespaceRegex().Replace(query.Trim(), " ");
        string corrected = CorrectCommonSearchTypos(trimmed);
        bool correctedQuery = !string.Equals(corrected, trimmed, StringComparison.OrdinalIgnoreCase);
        bool vehicleQuery = LooksLikeVehicleQuery(corrected);
        bool hasPriceOrSpecs = ContainsAny(corrected, ["price", "prices", "value", "market", "spec", "specs", "review"]);
        bool hasMarketTerms = ContainsAny(corrected, ["used", "sale", "market", "classic.com", "carsforsale"]);

        var variants = new List<string>();

        if (correctedQuery && vehicleQuery && !hasPriceOrSpecs)
        {
            variants.Add(corrected + " price specs review");
        }

        if (correctedQuery && vehicleQuery && !hasMarketTerms)
        {
            variants.Add(corrected + " used price market value");
        }

        if (correctedQuery)
        {
            variants.Add(corrected);
        }

        variants.Add(trimmed);

        if (!correctedQuery && vehicleQuery && !hasPriceOrSpecs)
        {
            variants.Add(corrected + " price specs review");
        }

        if (!correctedQuery && vehicleQuery && !hasMarketTerms)
        {
            variants.Add(corrected + " used price market value");
        }

        return variants
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }

    public static IReadOnlyList<SearchResult> ParseSearchHtml(string html)
    {
        List<SearchResult> results = [];
        results.AddRange(ParseStartpageHtml(html));

        if (results.Count == 0)
        {
            results.AddRange(ParseMojeekHtml(html));
        }

        if (results.Count == 0)
        {
            results.AddRange(ParseDuckDuckGoLiteHtml(html));
        }

        return Deduplicate(results);
    }

    public static IReadOnlyList<SearchResult> ParseDuckDuckGoHtml(string html)
    {
        return ParseSearchHtml(html);
    }

    private static IEnumerable<SearchResult> ParseStartpageHtml(string html)
    {
        foreach (Match match in StartpageResultRegex().Matches(html))
        {
            string url = WebUtility.HtmlDecode(match.Groups[1].Value);
            string title = CleanHtml(match.Groups[2].Value);
            string snippet = CleanHtml(match.Groups[3].Value);

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
            {
                yield return new SearchResult(title, url, snippet);
            }
        }
    }

    private static IEnumerable<SearchResult> ParseMojeekHtml(string html)
    {
        foreach (Match match in MojeekResultRegex().Matches(html))
        {
            string url = WebUtility.HtmlDecode(match.Groups[1].Value);
            string title = CleanHtml(match.Groups[2].Value);
            string snippet = CleanHtml(match.Groups[3].Value);

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
            {
                yield return new SearchResult(title, url, snippet);
            }
        }
    }

    private static IEnumerable<SearchResult> ParseDuckDuckGoLiteHtml(string html)
    {
        foreach (Match match in DuckDuckGoLiteResultRegex().Matches(html))
        {
            string rawUrl = WebUtility.HtmlDecode(match.Groups[1].Value);
            string title = CleanHtml(match.Groups[2].Value);

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(rawUrl))
            {
                yield return new SearchResult(title, NormalizeDuckDuckGoUrl(rawUrl), string.Empty);
            }
        }
    }

    private static IReadOnlyList<SearchResult> Deduplicate(IEnumerable<SearchResult> results)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<SearchResult>();

        foreach (SearchResult result in results)
        {
            if (seen.Add(result.Url))
            {
                unique.Add(result);
            }
        }

        return unique;
    }

    public static string RenderResults(string originalQuery, string searchedQuery, IReadOnlyList<SearchResult> results, string provider, IReadOnlyList<PageExtract>? pageExtracts = null)
    {
        string correctionNote = string.Equals(originalQuery, searchedQuery, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $"\nCorrected/expanded query used: {searchedQuery}";

        string renderedResults = string.Join("\n", results.Select((x, index) =>
        {
            string snippet = string.IsNullOrWhiteSpace(x.Snippet) ? string.Empty : $"\n   {x.Snippet}";
            return $"{index + 1}. {x.Title}\n   {x.Url}{snippet}";
        }));

        string renderedExtracts = pageExtracts is { Count: > 0 }
            ? "\n\nRead page extracts:\n" + string.Join("\n", pageExtracts.Select((x, index) =>
                $"{index + 1}. {x.Title}\n   {x.Url}\n   Extract: {x.Text}"))
            : "\n\nRead page extracts: none available from the top links.";

        return "Search results for: " + originalQuery + correctionNote + $"\nProvider: {provider}\n" + renderedResults + renderedExtracts;
    }

    private async Task<IReadOnlyList<PageExtract>> ReadTopResultPagesAsync(IReadOnlyList<SearchResult> results, CancellationToken cancellationToken)
    {
        var extracts = new List<PageExtract>();

        foreach (SearchResult result in results.Take(3))
        {
            if (!Uri.TryCreate(result.Url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            {
                continue;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                ApplyBrowserLikeHeaders(request);

                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase) && !contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string html = await response.Content.ReadAsStringAsync(cancellationToken);
                if (LooksLikeBotChallenge(html))
                {
                    continue;
                }

                string text = ExtractReadablePageText(html, maxCharacters: 900);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    extracts.Add(new PageExtract(result.Title, result.Url, text));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                // Search results are still useful if a result page blocks or times out.
            }
        }

        return extracts;
    }

    public static string ExtractReadablePageText(string html, int maxCharacters = 900)
    {
        string withoutNoise = ScriptStyleRegex().Replace(html, " ");
        string text = CleanHtml(withoutNoise);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        if (text.Length > maxCharacters)
        {
            text = text[..maxCharacters].TrimEnd() + "...";
        }

        return text;
    }

    private static string CorrectCommonSearchTypos(string query)
    {
        string corrected = Regex.Replace(query, "\\bMitsubateie\\b", "Mitsubishi", RegexOptions.IgnoreCase);
        corrected = Regex.Replace(corrected, "\\bMitsubatie\\b", "Mitsubishi", RegexOptions.IgnoreCase);
        corrected = Regex.Replace(corrected, "\\bMitsubashi\\b", "Mitsubishi", RegexOptions.IgnoreCase);
        corrected = Regex.Replace(corrected, "\\bMitsubisi\\b", "Mitsubishi", RegexOptions.IgnoreCase);
        corrected = Regex.Replace(corrected, "\\bLanser\\b", "Lancer", RegexOptions.IgnoreCase);
        return corrected;
    }

    private static bool LooksLikeVehicleQuery(string query)
    {
        return ContainsAny(query, ["mitsubishi", "lancer", "car", "vehicle", "sedan", "coupe", "wagon", "engine"])
            || YearRegex().IsMatch(query);
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> words)
    {
        return words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyBrowserLikeHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        request.Headers.Referrer = new Uri("https://www.startpage.com/");
    }

    private static bool LooksLikeBotChallenge(string html)
    {
        return html.Contains("anomaly.js", StringComparison.OrdinalIgnoreCase)
            || html.Contains("captcha", StringComparison.OrdinalIgnoreCase)
            || html.Contains("botnet", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanHtml(string value)
    {
        string noTags = TagRegex().Replace(value, string.Empty);
        return WebUtility.HtmlDecode(noTags)
            .Replace("&middot;", "·", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string NormalizeDuckDuckGoUrl(string rawUrl)
    {
        if (rawUrl.StartsWith("//", StringComparison.Ordinal))
        {
            rawUrl = "https:" + rawUrl;
        }

        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri? uri))
        {
            string? uddg = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("uddg");
            if (!string.IsNullOrWhiteSpace(uddg))
            {
                return uddg;
            }
        }

        return rawUrl;
    }

    [GeneratedRegex("<a[^>]+class=\\\"[^\\\"]*result-title[^\\\"]*result-link[^\\\"]*\\\"[^>]+href=\\\"([^\\\"]+)\\\"[^>]*>\\s*<h2[^>]*>(.*?)</h2>\\s*</a>.*?<p[^>]+class=\\\"[^\\\"]*description[^\\\"]*\\\"[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StartpageResultRegex();

    [GeneratedRegex("<h2>\\s*<a[^>]+class=\\\"title\\\"[^>]+href=\\\"([^\\\"]+)\\\"[^>]*>(.*?)</a>\\s*</h2>\\s*<p[^>]+class=\\\"s\\\"[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MojeekResultRegex();

    [GeneratedRegex("<a[^>]+rel=\\\"nofollow\\\"[^>]+href=\\\"([^\\\"]+)\\\"[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DuckDuckGoLiteResultRegex();

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex("<(script|style|noscript)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex("\\s+", RegexOptions.Singleline)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("\\b(19|20)\\d{2}\\b", RegexOptions.Singleline)]
    private static partial Regex YearRegex();
}

public sealed record SearchResult(string Title, string Url, string Snippet);
public sealed record PageExtract(string Title, string Url, string Text);
