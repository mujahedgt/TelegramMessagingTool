using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Tools.GitHub;

public sealed class GitHubCommentIssueRequestTool : IApprovalRequestTool
{
    private const int MaxBodyLength = 4_000;

    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;

    public GitHubCommentIssueRequestTool(PendingActionService pendingActionService, BotSettings settings)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
    }

    public string Name => "github_comment_issue";

    public string Description => "Approval-gated GitHub issue comment for an allowed repository. Strict JSON only: {\"owner\":\"owner\",\"repo\":\"repo\",\"number\":123,\"body\":\"comment body\"}. Owner/repo can be omitted to use the configured default repo.";

    public bool RequiresApproval => true;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Fail(
            "github_comment_issue requires an authenticated pending-action context. Use it from the Telegram/console agent flow so it can create an approval request."));
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

        if (!TryParseInput(input, _settings.GitHub, out GitHubCommentIssuePayload payload, out string error))
        {
            return ToolResult.Fail(error);
        }

        PendingAction pendingAction = await _pendingActionService.CreateAsync(
            dbContext,
            user,
            toolName: Name,
            description: $"Comment on GitHub issue {payload.Owner}/{payload.Repo}#{payload.Number}",
            payloadJson: JsonSerializer.Serialize(payload),
            riskLevel: "high",
            ttl: TimeSpan.FromMinutes(15),
            cancellationToken: cancellationToken);

        return ToolResult.Ok($"""
Approval required.

Pending action #{pendingAction.Id}
Type: github_comment_issue
Risk: high
Repository: {payload.Owner}/{payload.Repo}
Issue: #{payload.Number}
Comment length: {payload.Body.Length} characters
Expires UTC: {pendingAction.ExpiresAt:yyyy-MM-dd HH:mm}

This request only created a pending action. It did not call GitHub or post a comment yet.
Approve: /approve {pendingAction.Id}
Deny: /deny {pendingAction.Id}
""");
    }

    public static bool TryParseInput(string input, GitHubSettings settings, out GitHubCommentIssuePayload payload, out string error)
    {
        payload = GitHubCommentIssuePayload.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "github_comment_issue input must be strict JSON with number and body.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "github_comment_issue input must be a JSON object.";
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
                error = "github_comment_issue input must include both owner and repo, or neither to use the default repo.";
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

            int number = ReadInt(root, "number");
            if (number <= 0)
            {
                error = "github_comment_issue requires a positive issue number.";
                return false;
            }

            string body = ReadString(root, "body").Trim();
            if (string.IsNullOrWhiteSpace(body))
            {
                error = "github_comment_issue requires a non-empty body.";
                return false;
            }

            if (body.Length > MaxBodyLength)
            {
                body = body[..MaxBodyLength];
            }

            payload = new GitHubCommentIssuePayload(
                Action: "github_comment_issue",
                Owner: owner,
                Repo: repo,
                Number: number,
                Body: body,
                RequestedAtUtc: DateTime.UtcNow);
            return true;
        }
        catch (JsonException)
        {
            error = "github_comment_issue input must be valid JSON, for example {\"number\":123,\"body\":\"Comment text\"}.";
            return false;
        }
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.TryGetInt32(out int value)
            ? value
            : 0;
    }
}

public sealed record GitHubCommentIssuePayload(
    string Action,
    string Owner,
    string Repo,
    int Number,
    string Body,
    DateTime RequestedAtUtc)
{
    public static GitHubCommentIssuePayload Empty { get; } = new(string.Empty, string.Empty, string.Empty, 0, string.Empty, DateTime.MinValue);
}

public interface IGitHubIssueCommenter
{
    Task<GitHubIssueCommentResult> CommentAsync(GitHubCommentIssuePayload payload, CancellationToken cancellationToken);
}

public sealed class SystemGitHubIssueCommenter : IGitHubIssueCommenter
{
    public async Task<GitHubIssueCommentResult> CommentAsync(GitHubCommentIssuePayload payload, CancellationToken cancellationToken)
    {
        string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return GitHubIssueCommentResult.Fail("Execution failed: GITHUB_TOKEN is not configured for GitHub issue comments.");
        }

        using var httpClient = new HttpClient();
        string uri = $"https://api.github.com/repos/{Uri.EscapeDataString(payload.Owner)}/{Uri.EscapeDataString(payload.Repo)}/issues/{payload.Number}/comments";
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.UserAgent.ParseAdd("TelegramMessagingTool/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        request.Content = new StringContent(JsonSerializer.Serialize(new { body = payload.Body }), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return GitHubIssueCommentResult.Fail($"GitHub issue comment failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Token is never shown in execution output.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            string url = root.TryGetProperty("html_url", out JsonElement urlElement) && urlElement.ValueKind == JsonValueKind.String
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;
            return GitHubIssueCommentResult.Ok(url, $"Created GitHub issue comment on #{payload.Number}: {url}");
        }
        catch (JsonException ex)
        {
            return GitHubIssueCommentResult.Fail($"GitHub issue comment returned invalid JSON: {ex.Message}");
        }
    }
}

public sealed record GitHubIssueCommentResult(bool Success, string Url, string Message)
{
    public static GitHubIssueCommentResult Ok(string url, string message) => new(true, url, message);

    public static GitHubIssueCommentResult Fail(string message) => new(false, string.Empty, message);
}
