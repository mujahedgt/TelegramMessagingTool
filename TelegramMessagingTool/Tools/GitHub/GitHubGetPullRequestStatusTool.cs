using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TelegramMessagingTool.Tools.GitHub;

public sealed class GitHubGetPullRequestStatusTool : IAgentTool
{
    private readonly HttpClient _httpClient;
    private readonly GitHubSettings _settings;

    public GitHubGetPullRequestStatusTool(HttpClient httpClient, GitHubSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public string Name => "github_get_pr_status";

    public string Description => "Read-only GitHub pull request status summary for an allowed repository. Input JSON: {\"number\":123} or {\"owner\":\"owner\",\"repo\":\"repo\",\"number\":123}. Empty owner/repo uses the configured default repo.";

    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        PullRequestStatusRequest parsed = ParseInput(input);
        if (parsed.Error is not null)
        {
            return ToolResult.Fail(parsed.Error);
        }

        if (!GitHubRepoPolicy.IsAllowed(parsed.Owner, parsed.Repo, _settings.AllowedRepos))
        {
            return ToolResult.Fail($"Repository '{parsed.Owner}/{parsed.Repo}' is not in GITHUB_ALLOWED_REPOS.");
        }

        string uri = $"https://api.github.com/repos/{Uri.EscapeDataString(parsed.Owner)}/{Uri.EscapeDataString(parsed.Repo)}/pulls/{parsed.Number}";
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
            return ToolResult.Fail($"GitHub PR status lookup failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Token is never shown in tool output.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ToolResult.Fail("GitHub PR status lookup returned unexpected JSON. Expected an object.");
            }

            int number = ReadInt(root, "number", parsed.Number);
            string title = ReadString(root, "title", "(untitled)");
            string state = ReadString(root, "state", "unknown");
            string url = ReadString(root, "html_url", $"https://github.com/{parsed.Owner}/{parsed.Repo}/pull/{number}");
            string author = ReadNestedString(root, "user", "login", "unknown");
            string createdAt = ReadString(root, "created_at", "unknown");
            string updatedAt = ReadString(root, "updated_at", "unknown");
            string head = ReadNestedString(root, "head", "ref", "unknown");
            string baseRef = ReadNestedString(root, "base", "ref", "unknown");
            string headSha = ReadNestedString(root, "head", "sha", "unknown");
            string mergeable = ReadNullableBool(root, "mergeable");
            string mergeableState = ReadString(root, "mergeable_state", "unknown");
            string draft = ReadBool(root, "draft") ? "draft" : "ready";
            string merged = ReadBool(root, "merged") ? "yes" : "no";
            int comments = ReadInt(root, "comments", 0);
            int reviewComments = ReadInt(root, "review_comments", 0);
            int commits = ReadInt(root, "commits", 0);
            int additions = ReadInt(root, "additions", 0);
            int deletions = ReadInt(root, "deletions", 0);
            int changedFiles = ReadInt(root, "changed_files", 0);
            string requestedReviewers = ReadLoginArray(root, "requested_reviewers", "none");

            var output = new StringBuilder();
            output.AppendLine($"GitHub PR status: {parsed.Owner}/{parsed.Repo} #{number} [{state}] {title}");
            output.AppendLine($"Author: {author}");
            output.AppendLine($"Branches: {head} -> {baseRef}");
            output.AppendLine($"Head SHA: {headSha}");
            output.AppendLine($"Draft: {draft}");
            output.AppendLine($"Merged: {merged}");
            output.AppendLine($"Mergeable: {mergeable} ({mergeableState})");
            output.AppendLine($"Commits: {commits}");
            output.AppendLine($"Changes: +{additions} -{deletions} across {changedFiles} files");
            output.AppendLine($"Conversation: {comments} comments, {reviewComments} review comments");
            output.AppendLine($"Requested reviewers: {requestedReviewers}");
            output.AppendLine($"Created: {createdAt}");
            output.AppendLine($"Updated: {updatedAt}");
            output.AppendLine($"URL: {url}");

            return ToolResult.Ok(output.ToString().TrimEnd());
        }
        catch (JsonException ex)
        {
            return ToolResult.Fail($"GitHub PR status lookup returned invalid JSON: {ex.Message}");
        }
    }

    private PullRequestStatusRequest ParseInput(string input)
    {
        string owner = _settings.DefaultOwner;
        string repo = _settings.DefaultRepo;

        if (string.IsNullOrWhiteSpace(input))
        {
            return PullRequestStatusRequest.Failed("github_get_pr_status input must include a PR number, for example {\"number\":123}.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return PullRequestStatusRequest.Failed("github_get_pr_status input must be a JSON object.");
            }

            string inputOwner = ReadString(root, "owner", string.Empty).Trim();
            string inputRepo = ReadString(root, "repo", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(inputOwner) || !string.IsNullOrWhiteSpace(inputRepo))
            {
                if (string.IsNullOrWhiteSpace(inputOwner) || string.IsNullOrWhiteSpace(inputRepo))
                {
                    return PullRequestStatusRequest.Failed("github_get_pr_status input must include both owner and repo, or neither to use the default repo.");
                }

                owner = inputOwner;
                repo = inputRepo;
            }

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return PullRequestStatusRequest.Failed("No repository input was provided and GITHUB_DEFAULT_OWNER/GITHUB_DEFAULT_REPO are not configured.");
            }

            if (!root.TryGetProperty("number", out JsonElement numberElement) || !numberElement.TryGetInt32(out int number) || number < 1)
            {
                return PullRequestStatusRequest.Failed("github_get_pr_status input must include a positive integer PR number.");
            }

            return new PullRequestStatusRequest(owner, repo, number, null);
        }
        catch (JsonException)
        {
            return PullRequestStatusRequest.Failed("github_get_pr_status input must be valid JSON, for example {\"owner\":\"mujahedgt\",\"repo\":\"TelegramMessagingTool\",\"number\":123}.");
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

    private static int ReadInt(JsonElement root, string propertyName, int fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.TryGetInt32(out int value) ? value : fallback;
    }

    private static bool ReadBool(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.True;
    }

    private static string ReadNullableBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element))
        {
            return "unknown";
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "unknown",
            _ => "unknown"
        };
    }

    private static string ReadLoginArray(JsonElement root, string propertyName, string fallback)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        List<string> names = [];
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("login", out JsonElement login)
                && login.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(login.GetString()))
            {
                names.Add(login.GetString()!);
            }
        }

        return names.Count == 0 ? fallback : string.Join(", ", names);
    }

    private sealed record PullRequestStatusRequest(string Owner, string Repo, int Number, string? Error)
    {
        public static PullRequestStatusRequest Failed(string error) => new(string.Empty, string.Empty, 0, error);
    }
}
