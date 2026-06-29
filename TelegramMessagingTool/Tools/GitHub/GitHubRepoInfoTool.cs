using System.Net.Http.Headers;
using System.Text.Json;

namespace TelegramMessagingTool.Tools.GitHub;

public sealed class GitHubRepoInfoTool : IAgentTool
{
    private readonly HttpClient _httpClient;
    private readonly GitHubSettings _settings;

    public GitHubRepoInfoTool(HttpClient httpClient, GitHubSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public string Name => "github_repo_info";

    public string Description => "Read-only GitHub repository metadata for an allowed repository. Input JSON optional: {\"owner\":\"owner\",\"repo\":\"repo\"}. Empty input uses the configured default repo.";

    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        (string owner, string repo, string? error) = ParseInput(input);
        if (error is not null)
        {
            return ToolResult.Fail(error);
        }

        if (!GitHubRepoPolicy.IsAllowed(owner, repo, _settings.AllowedRepos))
        {
            return ToolResult.Fail($"Repository '{owner}/{repo}' is not in GITHUB_ALLOWED_REPOS.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}");
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
            return ToolResult.Fail($"GitHub repo lookup failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Token is never shown in tool output.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string fullName = ReadString(root, "full_name", $"{owner}/{repo}");
            string description = ReadString(root, "description", "");
            string visibility = ReadString(root, "visibility", ReadBool(root, "private") ? "private" : "public");
            string defaultBranch = ReadString(root, "default_branch", "unknown");
            int stars = ReadInt(root, "stargazers_count");
            int forks = ReadInt(root, "forks_count");
            int openIssues = ReadInt(root, "open_issues_count");
            string htmlUrl = ReadString(root, "html_url", $"https://github.com/{owner}/{repo}");
            string pushedAt = ReadString(root, "pushed_at", "unknown");

            string output = $"""
GitHub repository: {fullName}
Description: {(string.IsNullOrWhiteSpace(description) ? "(none)" : description)}
Visibility: {visibility}
Default branch: {defaultBranch}
Stars: {stars}
Forks: {forks}
Open issues/PRs count: {openIssues}
Last pushed: {pushedAt}
URL: {htmlUrl}
""";
            return ToolResult.Ok(output);
        }
        catch (JsonException ex)
        {
            return ToolResult.Fail($"GitHub repo lookup returned invalid JSON: {ex.Message}");
        }
    }

    private (string Owner, string Repo, string? Error) ParseInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            if (string.IsNullOrWhiteSpace(_settings.DefaultOwner) || string.IsNullOrWhiteSpace(_settings.DefaultRepo))
            {
                return (string.Empty, string.Empty, "No repository input was provided and GITHUB_DEFAULT_OWNER/GITHUB_DEFAULT_REPO are not configured.");
            }

            return (_settings.DefaultOwner, _settings.DefaultRepo, null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (string.Empty, string.Empty, "github_repo_info input must be a JSON object.");
            }

            string owner = ReadString(root, "owner", string.Empty);
            string repo = ReadString(root, "repo", string.Empty);
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return (string.Empty, string.Empty, "github_repo_info input must include owner and repo strings, or be empty to use the default repo.");
            }

            return (owner.Trim(), repo.Trim(), null);
        }
        catch (JsonException)
        {
            return (string.Empty, string.Empty, "github_repo_info input must be valid JSON, for example {\"owner\":\"mujahedgt\",\"repo\":\"TelegramMessagingTool\"}.");
        }
    }

    private static string ReadString(JsonElement root, string propertyName, string fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? fallback
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
}
