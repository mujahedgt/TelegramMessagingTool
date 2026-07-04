using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TelegramMessagingTool.Tools.GitHub;

public sealed class GitHubGetIssueTool : IAgentTool
{
    private const int MaxBodyChars = 1200;

    private readonly HttpClient _httpClient;
    private readonly GitHubSettings _settings;

    public GitHubGetIssueTool(HttpClient httpClient, GitHubSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public string Name => "github_get_issue";

    public string Description => "Read-only GitHub issue detail lookup for an allowed repository. Input JSON: {\"number\":123} or {\"owner\":\"owner\",\"repo\":\"repo\",\"number\":123}. Empty owner/repo uses the configured default repo.";

    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        IssueDetailRequest parsed = ParseInput(input);
        if (parsed.Error is not null)
        {
            return ToolResult.Fail(parsed.Error);
        }

        if (!GitHubRepoPolicy.IsAllowed(parsed.Owner, parsed.Repo, _settings.AllowedRepos))
        {
            return ToolResult.Fail($"Repository '{parsed.Owner}/{parsed.Repo}' is not in GITHUB_ALLOWED_REPOS.");
        }

        string uri = $"https://api.github.com/repos/{Uri.EscapeDataString(parsed.Owner)}/{Uri.EscapeDataString(parsed.Repo)}/issues/{parsed.Number}";
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
            return ToolResult.Fail($"GitHub issue lookup failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Token is never shown in tool output.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ToolResult.Fail("GitHub issue lookup returned unexpected JSON. Expected an object.");
            }

            if (root.TryGetProperty("pull_request", out _))
            {
                return ToolResult.Fail($"#{parsed.Number} in {parsed.Owner}/{parsed.Repo} is a pull request, not an issue. Use a PR tool when that is implemented.");
            }

            int number = ReadInt(root, "number", parsed.Number);
            string title = ReadString(root, "title", "(untitled)");
            string state = ReadString(root, "state", "unknown");
            string url = ReadString(root, "html_url", $"https://github.com/{parsed.Owner}/{parsed.Repo}/issues/{number}");
            string createdAt = ReadString(root, "created_at", "unknown");
            string updatedAt = ReadString(root, "updated_at", "unknown");
            string author = ReadNestedString(root, "user", "login", "unknown");
            string labels = ReadNameArray(root, "labels", "none");
            string assignees = ReadNameArray(root, "assignees", "none", nameProperty: "login");
            int comments = ReadInt(root, "comments", 0);
            string issueBody = TruncateBody(ReadString(root, "body", string.Empty));

            var output = new StringBuilder();
            output.AppendLine($"GitHub issue: {parsed.Owner}/{parsed.Repo} #{number} [{state}] {title}");
            output.AppendLine($"Author: {author}");
            output.AppendLine($"Assignees: {assignees}");
            output.AppendLine($"Labels: {labels}");
            output.AppendLine($"Comments: {comments}");
            output.AppendLine($"Created: {createdAt}");
            output.AppendLine($"Updated: {updatedAt}");
            output.AppendLine($"URL: {url}");
            output.AppendLine("Body:");
            output.AppendLine(string.IsNullOrWhiteSpace(issueBody) ? "(empty)" : issueBody);

            return ToolResult.Ok(output.ToString().TrimEnd());
        }
        catch (JsonException ex)
        {
            return ToolResult.Fail($"GitHub issue lookup returned invalid JSON: {ex.Message}");
        }
    }

    private IssueDetailRequest ParseInput(string input)
    {
        string owner = _settings.DefaultOwner;
        string repo = _settings.DefaultRepo;

        if (string.IsNullOrWhiteSpace(input))
        {
            return IssueDetailRequest.Failed("github_get_issue input must include an issue number, for example {\"number\":123}.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return IssueDetailRequest.Failed("github_get_issue input must be a JSON object.");
            }

            string inputOwner = ReadString(root, "owner", string.Empty).Trim();
            string inputRepo = ReadString(root, "repo", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(inputOwner) || !string.IsNullOrWhiteSpace(inputRepo))
            {
                if (string.IsNullOrWhiteSpace(inputOwner) || string.IsNullOrWhiteSpace(inputRepo))
                {
                    return IssueDetailRequest.Failed("github_get_issue input must include both owner and repo, or neither to use the default repo.");
                }

                owner = inputOwner;
                repo = inputRepo;
            }

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return IssueDetailRequest.Failed("No repository input was provided and GITHUB_DEFAULT_OWNER/GITHUB_DEFAULT_REPO are not configured.");
            }

            if (!root.TryGetProperty("number", out JsonElement numberElement) || !numberElement.TryGetInt32(out int number) || number < 1)
            {
                return IssueDetailRequest.Failed("github_get_issue input must include a positive integer issue number.");
            }

            return new IssueDetailRequest(owner, repo, number, null);
        }
        catch (JsonException)
        {
            return IssueDetailRequest.Failed("github_get_issue input must be valid JSON, for example {\"owner\":\"mujahedgt\",\"repo\":\"TelegramMessagingTool\",\"number\":123}.");
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

    private static string ReadNameArray(JsonElement root, string propertyName, string fallback, string nameProperty = "name")
    {
        if (!root.TryGetProperty(propertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        List<string> names = [];
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty(nameProperty, out JsonElement name)
                && name.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(name.GetString()))
            {
                names.Add(name.GetString()!);
            }
        }

        return names.Count == 0 ? fallback : string.Join(", ", names);
    }

    private static string TruncateBody(string body)
    {
        string normalized = body.Replace("\r\n", "\n").Trim();
        return normalized.Length <= MaxBodyChars ? normalized : normalized[..MaxBodyChars] + "…";
    }

    private sealed record IssueDetailRequest(string Owner, string Repo, int Number, string? Error)
    {
        public static IssueDetailRequest Failed(string error) => new(string.Empty, string.Empty, 0, error);
    }
}
