using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Tools.GitHub;

public sealed class GitHubCreateIssueRequestTool : IApprovalRequestTool
{
    private const int MaxTitleLength = 256;
    private const int MaxBodyLength = 4_000;
    private const int MaxLabels = 10;

    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;

    public GitHubCreateIssueRequestTool(PendingActionService pendingActionService, BotSettings settings)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
    }

    public string Name => "github_create_issue";

    public string Description => "Approval-gated GitHub issue creation for an allowed repository. Strict JSON only: {\"owner\":\"owner\",\"repo\":\"repo\",\"title\":\"issue title\",\"body\":\"issue body\",\"labels\":[\"bug\"]}. Owner/repo can be omitted to use the configured default repo.";

    public bool RequiresApproval => true;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Fail(
            "github_create_issue requires an authenticated pending-action context. Use it from the Telegram/console agent flow so it can create an approval request."));
    }

    public async Task<ToolResult> CreatePendingActionAsync(
        string input,
        TelegramDbContext dbContext,
        ConnectedUser user,
        CancellationToken cancellationToken)
    {
        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return ToolResult.Fail(BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId));
        }

        if (!_settings.GitHub.EnableGitHubWriteTools)
        {
            return ToolResult.Fail("GitHub write tools are disabled. Set ENABLE_GITHUB_WRITE_TOOLS=true to allow approval-backed GitHub writes.");
        }

        if (!TryParseInput(input, _settings.GitHub, out GitHubCreateIssuePayload payload, out string error))
        {
            return ToolResult.Fail(error);
        }

        PendingAction pendingAction = await _pendingActionService.CreateAsync(
            dbContext,
            user,
            toolName: Name,
            description: $"Create GitHub issue in {payload.Owner}/{payload.Repo}: {payload.Title}",
            payloadJson: JsonSerializer.Serialize(payload),
            riskLevel: "high",
            ttl: TimeSpan.FromMinutes(15),
            cancellationToken: cancellationToken);

        return ToolResult.Ok($"""
Approval required.

Pending action #{pendingAction.Id}
Type: github_create_issue
Risk: high
Repository: {payload.Owner}/{payload.Repo}
Title: {payload.Title}
Labels: {(payload.Labels.Count == 0 ? "none" : string.Join(", ", payload.Labels))}
Expires UTC: {pendingAction.ExpiresAt:yyyy-MM-dd HH:mm}

This request only created a pending action. It did not call GitHub or create an issue yet.
Approve: /approve {pendingAction.Id}
Deny: /deny {pendingAction.Id}
""");
    }

    public static bool TryParseInput(string input, GitHubSettings settings, out GitHubCreateIssuePayload payload, out string error)
    {
        payload = GitHubCreateIssuePayload.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "github_create_issue input must be strict JSON with title and body.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "github_create_issue input must be a JSON object.";
                return false;
            }

            string owner = ReadString(root, "owner").Trim();
            string repo = ReadString(root, "repo").Trim();
            if (string.IsNullOrWhiteSpace(owner) && string.IsNullOrWhiteSpace(repo))
            {
                owner = settings.DefaultOwner;
                repo = settings.DefaultRepo;
            }
            else if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                error = "github_create_issue input must include both owner and repo, or neither to use the default repo.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                error = "No repository input was provided and GITHUB_DEFAULT_OWNER/GITHUB_DEFAULT_REPO are not configured.";
                return false;
            }

            if (!GitHubRepoPolicy.IsAllowed(owner, repo, settings.AllowedRepos))
            {
                error = $"Repository '{owner}/{repo}' is not in GITHUB_ALLOWED_REPOS.";
                return false;
            }

            string title = ReadString(root, "title").Trim();
            string body = ReadString(root, "body").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                error = "github_create_issue requires a non-empty title.";
                return false;
            }

            if (title.Length > MaxTitleLength)
            {
                error = $"github_create_issue title must be {MaxTitleLength} characters or less.";
                return false;
            }

            if (body.Length > MaxBodyLength)
            {
                body = body[..MaxBodyLength];
            }

            IReadOnlyList<string> labels = ReadStringArray(root, "labels")
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxLabels)
                .ToList();

            payload = new GitHubCreateIssuePayload(
                Action: "github_create_issue",
                Owner: owner,
                Repo: repo,
                Title: title,
                Body: body,
                Labels: labels,
                RequestedAtUtc: DateTime.UtcNow);
            return true;
        }
        catch (JsonException)
        {
            error = "github_create_issue input must be valid JSON, for example {\"title\":\"Bug title\",\"body\":\"Details\",\"labels\":[\"bug\"]}.";
            return false;
        }
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                values.Add(item.GetString()!);
            }
        }

        return values;
    }
}

public sealed record GitHubCreateIssuePayload(
    string Action,
    string Owner,
    string Repo,
    string Title,
    string Body,
    IReadOnlyList<string> Labels,
    DateTime RequestedAtUtc)
{
    public static GitHubCreateIssuePayload Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, [], DateTime.MinValue);
}

public interface IGitHubIssueCreator
{
    Task<GitHubIssueCreateResult> CreateIssueAsync(GitHubCreateIssuePayload payload, CancellationToken cancellationToken);
}

public sealed class SystemGitHubIssueCreator : IGitHubIssueCreator
{
    public async Task<GitHubIssueCreateResult> CreateIssueAsync(GitHubCreateIssuePayload payload, CancellationToken cancellationToken)
    {
        string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return GitHubIssueCreateResult.Fail("Execution failed: GITHUB_TOKEN is not configured for GitHub issue creation.");
        }

        using var httpClient = new HttpClient();
        string uri = $"https://api.github.com/repos/{Uri.EscapeDataString(payload.Owner)}/{Uri.EscapeDataString(payload.Repo)}/issues";
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.UserAgent.ParseAdd("TelegramMessagingTool/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestBody = new Dictionary<string, object?>
        {
            ["title"] = payload.Title,
            ["body"] = payload.Body
        };
        if (payload.Labels.Count > 0)
        {
            requestBody["labels"] = payload.Labels;
        }

        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return GitHubIssueCreateResult.Fail($"GitHub issue creation failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Token is never shown in execution output.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            int number = root.TryGetProperty("number", out JsonElement numberElement) && numberElement.TryGetInt32(out int parsedNumber)
                ? parsedNumber
                : 0;
            string url = root.TryGetProperty("html_url", out JsonElement urlElement) && urlElement.ValueKind == JsonValueKind.String
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;
            return GitHubIssueCreateResult.Ok(number, url, $"Created GitHub issue #{number}: {url}");
        }
        catch (JsonException ex)
        {
            return GitHubIssueCreateResult.Fail($"GitHub issue creation returned invalid JSON: {ex.Message}");
        }
    }
}

public sealed record GitHubIssueCreateResult(bool Success, int Number, string Url, string Message)
{
    public static GitHubIssueCreateResult Ok(int number, string url, string message) => new(true, number, url, message);

    public static GitHubIssueCreateResult Fail(string message) => new(false, 0, string.Empty, message);
}
