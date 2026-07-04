using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TelegramMessagingTool.Tools.GitHub;

public sealed class GitHubListPullRequestsTool : IAgentTool
{
    private const int DefaultLimit = 10;
    private const int MaxLimit = 50;

    private readonly HttpClient _httpClient;
    private readonly GitHubSettings _settings;

    public GitHubListPullRequestsTool(HttpClient httpClient, GitHubSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public string Name => "github_list_prs";

    public string Description => "Read-only GitHub pull request listing for an allowed repository. Input JSON optional: {\"owner\":\"owner\",\"repo\":\"repo\",\"state\":\"open\",\"limit\":10}. Empty input uses the configured default repo and open PRs.";

    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        PullRequestListRequest parsed = ParseInput(input);
        if (parsed.Error is not null)
        {
            return ToolResult.Fail(parsed.Error);
        }

        if (!GitHubRepoPolicy.IsAllowed(parsed.Owner, parsed.Repo, _settings.AllowedRepos))
        {
            return ToolResult.Fail($"Repository '{parsed.Owner}/{parsed.Repo}' is not in GITHUB_ALLOWED_REPOS.");
        }

        string uri = $"https://api.github.com/repos/{Uri.EscapeDataString(parsed.Owner)}/{Uri.EscapeDataString(parsed.Repo)}/pulls?state={Uri.EscapeDataString(parsed.State)}&per_page={parsed.Limit}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("TelegramMessagingTool/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(_settings.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Token);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ToolResult.Fail($"GitHub PR listing failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Token is never shown in tool output.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return ToolResult.Fail("GitHub PR listing returned unexpected JSON. Expected an array.");
            }

            var lines = new List<string>();
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                int number = ReadInt(item, "number");
                string title = ReadString(item, "title", "(untitled)");
                string state = ReadString(item, "state", "unknown");
                string url = ReadString(item, "html_url", $"https://github.com/{parsed.Owner}/{parsed.Repo}/pull/{number}");
                string createdAt = ReadString(item, "created_at", "unknown");
                string updatedAt = ReadString(item, "updated_at", "unknown");
                string author = ReadNestedString(item, "user", "login", "unknown");
                string head = ReadNestedString(item, "head", "ref", "unknown");
                string baseRef = ReadNestedString(item, "base", "ref", "unknown");
                string draft = ReadBool(item, "draft") ? "draft" : "ready";
                lines.Add($"#{number} [{state}] {title} — {author} — {head} -> {baseRef} — {draft} — created {createdAt}, updated {updatedAt}\n{url}");
            }

            if (lines.Count == 0)
            {
                return ToolResult.Ok($"GitHub pull requests for {parsed.Owner}/{parsed.Repo}: no {parsed.State} PRs found.");
            }

            var output = new StringBuilder();
            output.AppendLine($"GitHub pull requests for {parsed.Owner}/{parsed.Repo} (state={parsed.State}, limit={parsed.Limit}):");
            foreach (string line in lines)
            {
                output.AppendLine(line);
            }

            return ToolResult.Ok(output.ToString().TrimEnd());
        }
        catch (JsonException ex)
        {
            return ToolResult.Fail($"GitHub PR listing returned invalid JSON: {ex.Message}");
        }
    }

    private PullRequestListRequest ParseInput(string input)
    {
        string owner = _settings.DefaultOwner;
        string repo = _settings.DefaultRepo;
        string state = "open";
        int limit = DefaultLimit;

        if (string.IsNullOrWhiteSpace(input))
        {
            return string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)
                ? PullRequestListRequest.Failed("No repository input was provided and GITHUB_DEFAULT_OWNER/GITHUB_DEFAULT_REPO are not configured.")
                : new PullRequestListRequest(owner, repo, state, limit, null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return PullRequestListRequest.Failed("github_list_prs input must be a JSON object.");
            }

            string inputOwner = ReadString(root, "owner", string.Empty).Trim();
            string inputRepo = ReadString(root, "repo", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(inputOwner) || !string.IsNullOrWhiteSpace(inputRepo))
            {
                if (string.IsNullOrWhiteSpace(inputOwner) || string.IsNullOrWhiteSpace(inputRepo))
                {
                    return PullRequestListRequest.Failed("github_list_prs input must include both owner and repo, or neither to use the default repo.");
                }

                owner = inputOwner;
                repo = inputRepo;
            }

            state = ReadString(root, "state", state).Trim().ToLowerInvariant();
            if (state is not ("open" or "closed" or "all"))
            {
                return PullRequestListRequest.Failed("github_list_prs state must be one of: open, closed, all.");
            }

            if (root.TryGetProperty("limit", out JsonElement limitElement) && limitElement.TryGetInt32(out int requestedLimit))
            {
                limit = Math.Clamp(requestedLimit, 1, MaxLimit);
            }

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return PullRequestListRequest.Failed("No repository input was provided and GITHUB_DEFAULT_OWNER/GITHUB_DEFAULT_REPO are not configured.");
            }

            return new PullRequestListRequest(owner, repo, state, limit, null);
        }
        catch (JsonException)
        {
            return PullRequestListRequest.Failed("github_list_prs input must be valid JSON, for example {\"owner\":\"mujahedgt\",\"repo\":\"TelegramMessagingTool\",\"state\":\"open\",\"limit\":10}.");
        }
    }

    private static string ReadString(JsonElement root, string propertyName, string fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? fallback
            : fallback;
    }

    private static string ReadNestedString(JsonElement root, string parentPropertyName, string childPropertyName, string fallback)
    {
        return root.TryGetProperty(parentPropertyName, out JsonElement parent)
            && parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(childPropertyName, out JsonElement child)
            && child.ValueKind == JsonValueKind.String
                ? child.GetString() ?? fallback
                : fallback;
    }

    private static int ReadInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.TryGetInt32(out int value) ? value : 0;
    }

    private static bool ReadBool(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.True;
    }

    private sealed record PullRequestListRequest(string Owner, string Repo, string State, int Limit, string? Error)
    {
        public static PullRequestListRequest Failed(string error) => new(string.Empty, string.Empty, string.Empty, 0, error);
    }
}
