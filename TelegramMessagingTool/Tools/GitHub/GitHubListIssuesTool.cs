using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TelegramMessagingTool.Tools.GitHub;

public sealed class GitHubListIssuesTool : IAgentTool
{
    private const int DefaultLimit = 10;
    private const int MaxLimit = 50;

    private readonly HttpClient _httpClient;
    private readonly GitHubSettings _settings;

    public GitHubListIssuesTool(HttpClient httpClient, GitHubSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public string Name => "github_list_issues";

    public string Description => "Read-only GitHub issue listing for an allowed repository. Input JSON optional: {\"owner\":\"owner\",\"repo\":\"repo\",\"state\":\"open\",\"limit\":10}. Empty input uses the configured default repo and open issues.";

    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        IssueListRequest parsed = ParseInput(input);
        if (parsed.Error is not null)
        {
            return ToolResult.Fail(parsed.Error);
        }

        if (!GitHubRepoPolicy.IsAllowed(parsed.Owner, parsed.Repo, _settings.AllowedRepos))
        {
            return ToolResult.Fail($"Repository '{parsed.Owner}/{parsed.Repo}' is not in GITHUB_ALLOWED_REPOS.");
        }

        string uri = $"https://api.github.com/repos/{Uri.EscapeDataString(parsed.Owner)}/{Uri.EscapeDataString(parsed.Repo)}/issues?state={Uri.EscapeDataString(parsed.State)}&per_page={parsed.Limit}";
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
            return ToolResult.Fail($"GitHub issue listing failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Token is never shown in tool output.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return ToolResult.Fail("GitHub issue listing returned unexpected JSON. Expected an array.");
            }

            var lines = new List<string>();
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || item.TryGetProperty("pull_request", out _))
                {
                    continue;
                }

                int number = ReadInt(item, "number");
                string title = ReadString(item, "title", "(untitled)");
                string state = ReadString(item, "state", "unknown");
                string url = ReadString(item, "html_url", $"https://github.com/{parsed.Owner}/{parsed.Repo}/issues/{number}");
                string createdAt = ReadString(item, "created_at", "unknown");
                string author = ReadNestedString(item, "user", "login", "unknown");
                lines.Add($"#{number} [{state}] {title} — {author} — {createdAt}\n{url}");
            }

            if (lines.Count == 0)
            {
                return ToolResult.Ok($"GitHub issues for {parsed.Owner}/{parsed.Repo}: no {parsed.State} issues found. Pull requests are excluded.");
            }

            var output = new StringBuilder();
            output.AppendLine($"GitHub issues for {parsed.Owner}/{parsed.Repo} (state={parsed.State}, limit={parsed.Limit}; pull requests excluded):");
            foreach (string line in lines)
            {
                output.AppendLine(line);
            }

            return ToolResult.Ok(output.ToString().TrimEnd());
        }
        catch (JsonException ex)
        {
            return ToolResult.Fail($"GitHub issue listing returned invalid JSON: {ex.Message}");
        }
    }

    private IssueListRequest ParseInput(string input)
    {
        string owner = _settings.DefaultOwner;
        string repo = _settings.DefaultRepo;
        string state = "open";
        int limit = DefaultLimit;

        if (string.IsNullOrWhiteSpace(input))
        {
            return string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)
                ? IssueListRequest.Failed("No repository input was provided and GITHUB_DEFAULT_OWNER/GITHUB_DEFAULT_REPO are not configured.")
                : new IssueListRequest(owner, repo, state, limit, null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return IssueListRequest.Failed("github_list_issues input must be a JSON object.");
            }

            string inputOwner = ReadString(root, "owner", string.Empty).Trim();
            string inputRepo = ReadString(root, "repo", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(inputOwner) || !string.IsNullOrWhiteSpace(inputRepo))
            {
                if (string.IsNullOrWhiteSpace(inputOwner) || string.IsNullOrWhiteSpace(inputRepo))
                {
                    return IssueListRequest.Failed("github_list_issues input must include both owner and repo, or neither to use the default repo.");
                }

                owner = inputOwner;
                repo = inputRepo;
            }

            state = ReadString(root, "state", state).Trim().ToLowerInvariant();
            if (state is not ("open" or "closed" or "all"))
            {
                return IssueListRequest.Failed("github_list_issues state must be one of: open, closed, all.");
            }

            if (root.TryGetProperty("limit", out JsonElement limitElement) && limitElement.TryGetInt32(out int requestedLimit))
            {
                limit = Math.Clamp(requestedLimit, 1, MaxLimit);
            }

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return IssueListRequest.Failed("No repository input was provided and GITHUB_DEFAULT_OWNER/GITHUB_DEFAULT_REPO are not configured.");
            }

            return new IssueListRequest(owner, repo, state, limit, null);
        }
        catch (JsonException)
        {
            return IssueListRequest.Failed("github_list_issues input must be valid JSON, for example {\"owner\":\"mujahedgt\",\"repo\":\"TelegramMessagingTool\",\"state\":\"open\",\"limit\":10}.");
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

    private sealed record IssueListRequest(string Owner, string Repo, string State, int Limit, string? Error)
    {
        public static IssueListRequest Failed(string error) => new(string.Empty, string.Empty, string.Empty, 0, error);
    }
}
