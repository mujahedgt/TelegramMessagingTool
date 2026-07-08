using Microsoft.EntityFrameworkCore;
using static TestAssert;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramMessagingTool;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Commands;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Plugins;
using TelegramMessagingTool.Runtime;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Telegram;
using TelegramMessagingTool.Tools;
using TelegramMessagingTool.Tools.CommandExecution;
using TelegramMessagingTool.Tools.GitHub;

// RED tests for upgrade helpers.
string ollamaStreamFixture = string.Join('\n',
[
    "{\"message\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"done\":false}",
    "{\"message\":{\"role\":\"assistant\",\"content\":\" world\"},\"done\":false}",
    "{\"done\":true}"
]);
AssertEqual("Hello world", OllamaChatClient.ParseStreamingAssistantContent(ollamaStreamFixture), "OllamaChatClient parses streaming assistant content chunks");
AssertEqual("Empty response from Ollama.", OllamaChatClient.ParseStreamingAssistantContent("{\"done\":true}"), "OllamaChatClient reports empty streaming responses clearly");
AssertEqual("Invalid response received from Ollama.", OllamaChatClient.ParseStreamingAssistantContent("not-json"), "OllamaChatClient reports invalid streaming chunks clearly");

AssertEqual("TelegramMessagingTool.Abstractions", typeof(IAgentTool).Assembly.GetName().Name, "IAgentTool lives in plugin abstraction assembly");
AssertEqual("TelegramMessagingTool.Abstractions", typeof(ToolResult).Assembly.GetName().Name, "ToolResult lives in plugin abstraction assembly");
List<string> observedRuntimeEvents = [];
var runtimeObservability = new RuntimeObservabilityService(observedRuntimeEvents.Add);
runtimeObservability.ToolCallRequested("calculator", ToolRiskLevel.Low, isReadOnly: true);
runtimeObservability.PendingActionCreated(99, "repo_commit_changes", "high");
runtimeObservability.ApprovalExecutionCompleted(99, "github_create_issue", executed: false, success: false, "GITHUB_TOKEN=*** should be hidden");
runtimeObservability.CallbackDecisionReceived("pending_action", "approve", 99, actorTelegramUserId: 123456, ownerChatId: 123456);
runtimeObservability.CallbackDecisionRejected("task", "done", 100, actorTelegramUserId: 777777, ownerChatId: 123456, "unauthorized_actor");
AssertTrue(observedRuntimeEvents.Any(x => x.Contains("TOOL_CALL", StringComparison.OrdinalIgnoreCase) && x.Contains("tool=calculator", StringComparison.OrdinalIgnoreCase) && x.Contains("risk=low", StringComparison.OrdinalIgnoreCase)), "Runtime observability logs tool call metadata without message content");
AssertTrue(observedRuntimeEvents.Any(x => x.Contains("PENDING_ACTION", StringComparison.OrdinalIgnoreCase) && x.Contains("id=99", StringComparison.OrdinalIgnoreCase) && x.Contains("tool=repo_commit_changes", StringComparison.OrdinalIgnoreCase)), "Runtime observability logs pending action creation");
AssertTrue(observedRuntimeEvents.Any(x => x.Contains("APPROVAL_EXECUTION", StringComparison.OrdinalIgnoreCase) && x.Contains("executed=false", StringComparison.OrdinalIgnoreCase) && x.Contains("success=false", StringComparison.OrdinalIgnoreCase)), "Runtime observability logs approval execution results");
AssertTrue(observedRuntimeEvents.Any(x => x.Contains("CALLBACK_DECISION", StringComparison.OrdinalIgnoreCase) && x.Contains("domain=pending_action", StringComparison.OrdinalIgnoreCase) && x.Contains("status=accepted", StringComparison.OrdinalIgnoreCase)), "Runtime observability logs accepted callback metadata");
AssertTrue(observedRuntimeEvents.Any(x => x.Contains("CALLBACK_DECISION", StringComparison.OrdinalIgnoreCase) && x.Contains("domain=task", StringComparison.OrdinalIgnoreCase) && x.Contains("status=rejected", StringComparison.OrdinalIgnoreCase) && x.Contains("reason=unauthorized_actor", StringComparison.OrdinalIgnoreCase)), "Runtime observability logs rejected callback metadata");
AssertFalse(string.Join('\n', observedRuntimeEvents).Contains("***", StringComparison.OrdinalIgnoreCase), "Runtime observability redacts token-like values from operational logs");
IAgentTool metadataSafeTool = new DateTimeTool();
AssertEqual(ToolRiskLevel.Low, metadataSafeTool.RiskLevel, "Safe tools expose low risk metadata");
AssertTrue(metadataSafeTool.IsReadOnly, "Safe tools expose read-only metadata");
AssertTrue(metadataSafeTool.SafetySummary.Length > 0, "Tools expose a safety summary");
IAgentTool metadataHighRiskTool = new FakeHighRiskTool();
AssertEqual(ToolRiskLevel.High, metadataHighRiskTool.RiskLevel, "Approval-backed tools expose high risk metadata by default");
AssertFalse(metadataHighRiskTool.IsReadOnly, "Approval-backed tools expose state-changing metadata by default");
ToolRegistry metadataRegistry = new([metadataSafeTool, metadataHighRiskTool]);
string metadataToolList = metadataRegistry.RenderToolList();
AssertTrue(metadataToolList.Contains("risk: high", StringComparison.OrdinalIgnoreCase), "Built-in tool risk metadata renders from the tool contract");
AssertTrue(metadataToolList.Contains("can change state", StringComparison.OrdinalIgnoreCase), "Built-in tool read-only metadata renders from the tool contract");
var previewAction = new PendingAction
{
    Id = 42,
    ToolName = "repo_replace_text",
    Description = "Replace text in TelegramMessagingTool/Program.cs. Reason: test preview",
    RiskLevel = "high",
    Status = PendingActionStatuses.Pending,
    CreatedAt = new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc),
    ExpiresAt = new DateTime(2026, 7, 5, 9, 15, 0, DateTimeKind.Utc),
    PayloadJson = "{\"action\":\"repo_replace_text\",\"path\":\"TelegramMessagingTool/Program.cs\",\"old_text\":\"old\",\"new_text\":\"newer text\",\"reason\":\"test preview\"}"
};
string pendingPreview = PendingActionPreviewFormatter.RenderListItem(previewAction);
AssertTrue(pendingPreview.Contains("Exact risk: high", StringComparison.OrdinalIgnoreCase), "/pending preview shows exact risk label");
AssertTrue(pendingPreview.Contains("Target file: TelegramMessagingTool/Program.cs", StringComparison.OrdinalIgnoreCase), "/pending preview shows target file");
AssertTrue(pendingPreview.Contains("Diff summary: -3/+10 chars", StringComparison.OrdinalIgnoreCase), "/pending preview shows diff summary for repo text replacement");
string actionPreview = PendingActionPreviewFormatter.RenderDetails(previewAction);
AssertTrue(actionPreview.Contains("Payload summary:", StringComparison.OrdinalIgnoreCase), "/action details show a payload summary section");
AssertTrue(actionPreview.Contains("Target file: TelegramMessagingTool/Program.cs", StringComparison.OrdinalIgnoreCase), "/action details show target details");
AssertTrue(!actionPreview.Contains("old_text", StringComparison.OrdinalIgnoreCase), "/action details avoid dumping raw repo edit payload fields");
var commitPreviewAction = new PendingAction
{
    Id = 43,
    ToolName = "repo_commit_changes",
    RiskLevel = "high",
    Status = PendingActionStatuses.Pending,
    ExpiresAt = new DateTime(2026, 7, 5, 9, 20, 0, DateTimeKind.Utc),
    PayloadJson = "{\"action\":\"repo_commit_changes\",\"message\":\"Add preview formatter\"}"
};
AssertTrue(PendingActionPreviewFormatter.RenderListItem(commitPreviewAction).Contains("Git command preview: git commit -m \"Add preview formatter\"", StringComparison.OrdinalIgnoreCase), "/pending preview shows git command preview for commits");
var githubPreviewAction = new PendingAction
{
    Id = 44,
    ToolName = "github_create_issue",
    RiskLevel = "high",
    Status = PendingActionStatuses.Pending,
    ExpiresAt = new DateTime(2026, 7, 5, 9, 25, 0, DateTimeKind.Utc),
    PayloadJson = "{\"action\":\"github_create_issue\",\"owner\":\"mujahedgt\",\"repo\":\"TelegramMessagingTool\",\"title\":\"Improve previews\",\"labels\":[\"enhancement\"]}"
};
string githubPreview = PendingActionPreviewFormatter.RenderListItem(githubPreviewAction);
AssertTrue(githubPreview.Contains("GitHub repository: mujahedgt/TelegramMessagingTool", StringComparison.OrdinalIgnoreCase), "/pending preview shows GitHub repo target");
AssertTrue(githubPreview.Contains("GitHub issue preview: create issue", StringComparison.OrdinalIgnoreCase), "/pending preview shows GitHub issue preview");
RepoSafetyScanResult secretDiffScan = RepoSafetyScanner.ScanDiff("""
diff --git a/README.md b/README.md
+TELEGRAM_BOT_TOKEN=123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZ_testtokenvalue
""");
AssertFalse(secretDiffScan.Allowed, "RepoSafetyScanner blocks token-like values in diffs before commit/release");
AssertTrue(secretDiffScan.Message.Contains("TELEGRAM_BOT_TOKEN", StringComparison.OrdinalIgnoreCase), "RepoSafetyScanner explains the secret-like pattern");
AssertFalse(RepoSafetyScanner.ScanChangedPaths([".env"]).Allowed, "RepoSafetyScanner rejects .env files");
AssertFalse(RepoSafetyScanner.ScanChangedPaths(["release/TelegramMessagingTool.exe"]).Allowed, "RepoSafetyScanner rejects release outputs");
AssertFalse(RepoSafetyScanner.ScanChangedPaths(["TelegramMessagingTool/appsettings.Production.json"]).Allowed, "RepoSafetyScanner rejects production settings files");
AssertTrue(RepoSafetyScanner.ScanChangedPaths(["README.md", "TelegramMessagingTool/Program.cs"]).Allowed, "RepoSafetyScanner allows normal source and docs paths");
var historyAction = new PendingAction
{
    Id = 45,
    ToolName = "repo_commit_changes",
    Description = "Commit approved repository changes.",
    RiskLevel = "high",
    Status = PendingActionStatuses.Approved,
    CreatedAt = new DateTime(2026, 7, 5, 9, 30, 0, DateTimeKind.Utc),
    DecidedAt = new DateTime(2026, 7, 5, 9, 31, 0, DateTimeKind.Utc),
    DecisionNote = "Committed 2 file(s) as abc1234: Add safety checks",
    PayloadJson = "{\"action\":\"repo_commit_changes\",\"message\":\"Add safety checks\"}"
};
string actionHistory = ActionHistoryFormatter.RenderRecent([historyAction]);
AssertTrue(actionHistory.Contains("Recent actions", StringComparison.OrdinalIgnoreCase), "/actions history has a title");
AssertTrue(actionHistory.Contains("#45 [approved] repo_commit_changes", StringComparison.OrdinalIgnoreCase), "/actions history includes status and type");
AssertTrue(actionHistory.Contains("Decision: Committed 2 file(s) as abc1234", StringComparison.OrdinalIgnoreCase), "/actions history includes a compact decision note");
AssertFalse(actionHistory.Contains("PayloadJson", StringComparison.OrdinalIgnoreCase), "/actions history avoids raw payload field dumps");

List<string> chunks = TelegramMessageFormatter.SplitForTelegram(new string('x', 9000), 4096).ToList();
AssertEqual(3, chunks.Count, "SplitForTelegram creates 3 chunks for 9000 chars");
AssertTrue(chunks.All(x => x.Length <= 4096), "SplitForTelegram respects Telegram limit");
AssertEqual(new string('x', 9000), string.Concat(chunks), "SplitForTelegram preserves content");

string redacted = TelegramMessageFormatter.RedactForLogs("hello\nworld\rtest");
AssertFalse(redacted.Contains('\n'), "RedactForLogs removes new lines");
AssertFalse(redacted.Contains('\r'), "RedactForLogs removes carriage returns");

var transientTelegramException = new HttpRequestException(
    "An error occurred while sending the request.",
    new IOException(
        "Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.",
        new SocketException(10054)));
AssertTrue(TelegramReceiverErrorClassifier.IsTransientNetworkError(transientTelegramException), "TelegramReceiverErrorClassifier detects socket reset as transient");
AssertTrue(TelegramReceiverErrorClassifier.Summarize(transientTelegramException).Contains("Long polling will continue automatically"), "TelegramReceiverErrorClassifier summarizes transient network errors without stack spam");
AssertFalse(TelegramReceiverErrorClassifier.IsTransientNetworkError(new InvalidOperationException("bad config")), "TelegramReceiverErrorClassifier does not hide non-network errors");

PluginTests.RunPluginManifestCompatibilityTests();
await PluginTests.RunSamplePluginToolTestsAsync();
ConfigurationTests.RunConfigurationTests();

BotSettings commandFactorySettings = BotConfiguration.LoadFromEnvironment();
CommandTests.RunCommandRouterFactoryTests(commandFactorySettings);

BotSettings appServicesSettings = commandFactorySettings with
{
    BotToken = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi",
    EnableRepoWriteTools = false
};
using AppServices appServices = AppServicesBuilder.Build(appServicesSettings);
AssertTrue(appServices.BotClient is not null, "AppServicesBuilder creates Telegram bot client");
AssertTrue(appServices.CommandRouter.Commands.Any(x => x.Name == "/help"), "AppServicesBuilder creates command router");
AssertTrue(appServices.TelegramUpdateHandler is not null, "AppServicesBuilder creates Telegram update handler");
AssertTrue(appServices.ConsoleInputHandler is not null, "AppServicesBuilder creates console input handler");
AssertTrue(appServices.TaskReminderLoop is not null, "AppServicesBuilder creates task reminder loop");
AssertTrue(appServices.ToolRegistry.Tools.Any(x => x.Name == "datetime"), "AppServicesBuilder creates tool registry");
AssertEqual(
    Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "UserFiles")),
    appServices.DocumentStorage.RootDirectory,
    "AppServicesBuilder uses project-root UserFiles storage");
AssertEqual(
    Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "ImportInbox")),
    appServices.ImportDirectory,
    "AppServicesBuilder uses project-root ImportInbox storage");

AssertTrue(PluginManifest.TryParse("""
{
  "id": "sample-plugin",
  "name": "Sample Plugin",
  "version": "1.0.0",
  "apiVersion": "1.0",
  "entryAssembly": "SamplePlugin.dll",
  "enabled": true,
  "riskLevel": "low",
  "allowedToolNames": ["sample_tool"]
}
""" ).Success, "PluginManifest parses a valid manifest");
AssertFalse(PluginManifest.IsValidToolName("BadTool"), "PluginManifest rejects uppercase tool names");
AssertFalse(PluginManifest.TryParse("""
{
  "id": "bad-plugin",
  "name": "Bad Plugin",
  "version": "1.0.0",
  "apiVersion": "1.0",
  "entryAssembly": "BadPlugin.dll",
  "enabled": true,
  "riskLevel": "low",
  "allowedToolNames": ["BadTool"]
}
""" ).Success, "PluginManifest rejects invalid allowed tool names");

string pluginTestRoot = Path.Combine(Path.GetTempPath(), $"TelegramMessagingTool_Plugins_{Guid.NewGuid():N}");
Directory.CreateDirectory(Path.Combine(pluginTestRoot, "SamplePlugin"));
Directory.CreateDirectory(Path.Combine(pluginTestRoot, "DuplicatePlugin"));
await File.WriteAllTextAsync(Path.Combine(pluginTestRoot, "SamplePlugin", "plugin.json"), """
{
  "id": "sample-plugin",
  "name": "Sample Plugin",
  "version": "1.0.0",
  "apiVersion": "1.0",
  "entryAssembly": "SamplePlugin.dll",
  "enabled": true,
  "riskLevel": "low",
  "allowedToolNames": ["sample_tool"]
}
""");
await File.WriteAllTextAsync(Path.Combine(pluginTestRoot, "DuplicatePlugin", "plugin.json"), """
{
  "id": "duplicate-plugin",
  "name": "Duplicate Plugin",
  "version": "1.0.0",
  "apiVersion": "1.0",
  "entryAssembly": "DuplicatePlugin.dll",
  "enabled": true,
  "riskLevel": "low",
  "allowedToolNames": ["sample_tool"]
}
""");
PluginScanResult pluginScanResult = new PluginManifestScanner().Scan(pluginTestRoot);
AssertEqual(1, pluginScanResult.Manifests.Count, "PluginManifestScanner skips duplicate tool names across manifests");
AssertTrue(pluginScanResult.Manifests[0].Manifest.AllowedToolNames.Contains("sample_tool"), "PluginManifestScanner keeps one plugin manifest for the duplicated tool name");
AssertTrue(pluginScanResult.Diagnostics.Any(x => x.Contains("duplicated", StringComparison.OrdinalIgnoreCase)), "PluginManifestScanner reports duplicate tool diagnostics");
AssertTrue(pluginScanResult.RenderSummary().Contains("plugin", StringComparison.OrdinalIgnoreCase), "PluginScanResult summary includes discovered plugin information");
Directory.Delete(pluginTestRoot, recursive: true);

IReadOnlySet<string> allowedGitHubRepos = GitHubRepoPolicy.ParseAllowedRepos("mujahedgt/TelegramMessagingTool, mujahedgt/IsolationForestServer, badformat");
AssertTrue(GitHubRepoPolicy.IsAllowed("mujahedgt", "TelegramMessagingTool", allowedGitHubRepos), "GitHubRepoPolicy allows configured repo");
AssertFalse(GitHubRepoPolicy.IsAllowed("other", "repo", allowedGitHubRepos), "GitHubRepoPolicy rejects repo outside allowlist");
AssertEqual("mujahedgt/TelegramMessagingTool", GitHubRepoPolicy.NormalizeFullName(" mujahedgt / TelegramMessagingTool "), "GitHubRepoPolicy normalizes owner/repo strings");

var fakeGitHubHandler = new FakeHttpMessageHandler("""
{
  "full_name": "mujahedgt/TelegramMessagingTool",
  "description": "Telegram Ollama bot",
  "visibility": "public",
  "default_branch": "master",
  "stargazers_count": 7,
  "forks_count": 1,
  "open_issues_count": 3,
  "html_url": "https://github.com/mujahedgt/TelegramMessagingTool",
  "pushed_at": "2026-06-29T00:00:00Z"
}
""");
using var fakeGitHubClient = new HttpClient(fakeGitHubHandler);
var gitHubSettings = new GitHubSettings(
    EnableGitHubTools: true,
    EnableGitHubWriteTools: false,
    Token: "secret-token-for-test",
    DefaultOwner: "mujahedgt",
    DefaultRepo: "TelegramMessagingTool",
    AllowedRepos: new HashSet<string>(["mujahedgt/TelegramMessagingTool"], StringComparer.OrdinalIgnoreCase));
var repoInfoTool = new GitHubRepoInfoTool(fakeGitHubClient, gitHubSettings);
ToolResult repoInfoResult = await repoInfoTool.ExecuteAsync(string.Empty, CancellationToken.None);
AssertTrue(repoInfoResult.Success, "GitHubRepoInfoTool succeeds for default allowed repo");
AssertTrue(repoInfoResult.Output.Contains("mujahedgt/TelegramMessagingTool"), "GitHubRepoInfoTool reports full repository name");
AssertTrue(repoInfoResult.Output.Contains("Default branch: master"), "GitHubRepoInfoTool reports default branch");
AssertFalse(repoInfoResult.Output.Contains("secret-token-for-test"), "GitHubRepoInfoTool never exposes token value");
AssertTrue(fakeGitHubHandler.LastRequestAuthorization?.StartsWith("Bearer ") == true, "GitHubRepoInfoTool sends bearer auth when token is configured");
ToolResult blockedRepoInfoResult = await repoInfoTool.ExecuteAsync("{\"owner\":\"other\",\"repo\":\"repo\"}", CancellationToken.None);
AssertFalse(blockedRepoInfoResult.Success, "GitHubRepoInfoTool rejects repositories outside allowlist");

var fakeGitHubIssuesHandler = new FakeHttpMessageHandler("""
[
  {
    "number": 42,
    "title": "Add GitHub issue listing",
    "state": "open",
    "html_url": "https://github.com/mujahedgt/TelegramMessagingTool/issues/42",
    "user": { "login": "mujahedgt" },
    "created_at": "2026-06-29T01:00:00Z",
    "pull_request": { "url": "https://api.github.com/repos/mujahedgt/TelegramMessagingTool/pulls/42" }
  },
  {
    "number": 41,
    "title": "Document GitHub tools",
    "state": "open",
    "html_url": "https://github.com/mujahedgt/TelegramMessagingTool/issues/41",
    "user": { "login": "mujahedgt" },
    "created_at": "2026-06-29T00:30:00Z"
  }
]
""");
using var fakeGitHubIssuesClient = new HttpClient(fakeGitHubIssuesHandler);
var listIssuesTool = new GitHubListIssuesTool(fakeGitHubIssuesClient, gitHubSettings);
ToolResult issuesResult = await listIssuesTool.ExecuteAsync("{\"state\":\"open\",\"limit\":10}", CancellationToken.None);
AssertTrue(issuesResult.Success, "GitHubListIssuesTool succeeds for default allowed repo");
AssertTrue(issuesResult.Output.Contains("#41"), "GitHubListIssuesTool includes normal issues");
AssertFalse(issuesResult.Output.Contains("#42"), "GitHubListIssuesTool excludes pull requests from issue output");
AssertFalse(issuesResult.Output.Contains("secret-token-for-test"), "GitHubListIssuesTool never exposes token value");
AssertTrue(fakeGitHubIssuesHandler.LastRequestUri?.ToString().Contains("/repos/mujahedgt/TelegramMessagingTool/issues") == true, "GitHubListIssuesTool calls the issues endpoint for the selected repo");
AssertTrue(fakeGitHubIssuesHandler.LastRequestUri?.ToString().Contains("state=open") == true, "GitHubListIssuesTool sends state query parameter");
AssertTrue(fakeGitHubIssuesHandler.LastRequestUri?.ToString().Contains("per_page=10") == true, "GitHubListIssuesTool sends bounded per_page query parameter");
ToolResult blockedIssuesResult = await listIssuesTool.ExecuteAsync("{\"owner\":\"other\",\"repo\":\"repo\"}", CancellationToken.None);
AssertFalse(blockedIssuesResult.Success, "GitHubListIssuesTool rejects repositories outside allowlist");
ToolResult badIssueStateResult = await listIssuesTool.ExecuteAsync("{\"state\":\"deleted\"}", CancellationToken.None);
AssertFalse(badIssueStateResult.Success, "GitHubListIssuesTool rejects unsupported issue states");

var fakeGitHubIssueDetailHandler = new FakeHttpMessageHandler("""
{
  "number": 41,
  "title": "Document GitHub tools",
  "state": "open",
  "html_url": "https://github.com/mujahedgt/TelegramMessagingTool/issues/41",
  "user": { "login": "mujahedgt" },
  "created_at": "2026-06-29T00:30:00Z",
  "updated_at": "2026-06-29T02:00:00Z",
  "comments": 2,
  "body": "Add README notes for the GitHub tools.",
  "labels": [
    { "name": "documentation" },
    { "name": "agent-tools" }
  ],
  "assignees": [
    { "login": "mujahedgt" }
  ]
}
""");
using var fakeGitHubIssueDetailClient = new HttpClient(fakeGitHubIssueDetailHandler);
var getIssueTool = new GitHubGetIssueTool(fakeGitHubIssueDetailClient, gitHubSettings);
ToolResult issueDetailResult = await getIssueTool.ExecuteAsync("{\"number\":41}", CancellationToken.None);
AssertTrue(issueDetailResult.Success, "GitHubGetIssueTool succeeds for default allowed repo");
AssertTrue(issueDetailResult.Output.Contains("#41 [open] Document GitHub tools"), "GitHubGetIssueTool renders issue number, state, and title");
AssertTrue(issueDetailResult.Output.Contains("documentation"), "GitHubGetIssueTool renders labels");
AssertTrue(issueDetailResult.Output.Contains("Comments: 2"), "GitHubGetIssueTool renders comment count");
AssertTrue(issueDetailResult.Output.Contains("Add README notes"), "GitHubGetIssueTool renders body excerpt");
AssertFalse(issueDetailResult.Output.Contains("secret-token-for-test"), "GitHubGetIssueTool never exposes token value");
AssertTrue(fakeGitHubIssueDetailHandler.LastRequestUri?.ToString().Contains("/repos/mujahedgt/TelegramMessagingTool/issues/41") == true, "GitHubGetIssueTool calls the issue detail endpoint");
ToolResult blockedIssueDetailResult = await getIssueTool.ExecuteAsync("{\"owner\":\"other\",\"repo\":\"repo\",\"number\":41}", CancellationToken.None);
AssertFalse(blockedIssueDetailResult.Success, "GitHubGetIssueTool rejects repositories outside allowlist");
ToolResult invalidIssueNumberResult = await getIssueTool.ExecuteAsync("{\"number\":0}", CancellationToken.None);
AssertFalse(invalidIssueNumberResult.Success, "GitHubGetIssueTool rejects invalid issue numbers");

var fakeGitHubPullRequestsHandler = new FakeHttpMessageHandler("""
[
  {
    "number": 12,
    "title": "Add GitHub PR listing",
    "state": "open",
    "html_url": "https://github.com/mujahedgt/TelegramMessagingTool/pull/12",
    "user": { "login": "mujahedgt" },
    "created_at": "2026-07-04T17:40:00Z",
    "updated_at": "2026-07-04T17:45:00Z",
    "draft": false,
    "head": { "ref": "feature/github-prs" },
    "base": { "ref": "master" }
  }
]
""");
using var fakeGitHubPullRequestsClient = new HttpClient(fakeGitHubPullRequestsHandler);
var listPullRequestsTool = new GitHubListPullRequestsTool(fakeGitHubPullRequestsClient, gitHubSettings);
ToolResult pullRequestsResult = await listPullRequestsTool.ExecuteAsync("{\"state\":\"open\",\"limit\":10}", CancellationToken.None);
AssertTrue(pullRequestsResult.Success, "GitHubListPullRequestsTool succeeds for default allowed repo");
AssertTrue(pullRequestsResult.Output.Contains("#12 [open] Add GitHub PR listing"), "GitHubListPullRequestsTool renders PR number, state, and title");
AssertTrue(pullRequestsResult.Output.Contains("feature/github-prs -> master"), "GitHubListPullRequestsTool renders head/base branches");
AssertFalse(pullRequestsResult.Output.Contains("secret-token-for-test"), "GitHubListPullRequestsTool never exposes token value");
AssertTrue(fakeGitHubPullRequestsHandler.LastRequestUri?.ToString().Contains("/repos/mujahedgt/TelegramMessagingTool/pulls") == true, "GitHubListPullRequestsTool calls the pulls endpoint for the selected repo");
AssertTrue(fakeGitHubPullRequestsHandler.LastRequestUri?.ToString().Contains("state=open") == true, "GitHubListPullRequestsTool sends state query parameter");
AssertTrue(fakeGitHubPullRequestsHandler.LastRequestUri?.ToString().Contains("per_page=10") == true, "GitHubListPullRequestsTool sends bounded per_page query parameter");
ToolResult blockedPullRequestsResult = await listPullRequestsTool.ExecuteAsync("{\"owner\":\"other\",\"repo\":\"repo\"}", CancellationToken.None);
AssertFalse(blockedPullRequestsResult.Success, "GitHubListPullRequestsTool rejects repositories outside allowlist");
ToolResult badPullRequestStateResult = await listPullRequestsTool.ExecuteAsync("{\"state\":\"merged\"}", CancellationToken.None);
AssertFalse(badPullRequestStateResult.Success, "GitHubListPullRequestsTool rejects unsupported PR states");

var fakeGitHubPullRequestStatusHandler = new FakeHttpMessageHandler("""
{
  "number": 12,
  "title": "Add GitHub PR listing",
  "state": "open",
  "html_url": "https://github.com/mujahedgt/TelegramMessagingTool/pull/12",
  "user": { "login": "mujahedgt" },
  "created_at": "2026-07-04T17:40:00Z",
  "updated_at": "2026-07-04T17:45:00Z",
  "draft": false,
  "mergeable": true,
  "mergeable_state": "clean",
  "merged": false,
  "comments": 1,
  "review_comments": 2,
  "commits": 3,
  "additions": 44,
  "deletions": 3,
  "changed_files": 6,
  "head": { "ref": "feature/github-prs", "sha": "abcdef1234567890" },
  "base": { "ref": "master" },
  "requested_reviewers": [
    { "login": "reviewer1" }
  ]
}
""");
using var fakeGitHubPullRequestStatusClient = new HttpClient(fakeGitHubPullRequestStatusHandler);
var getPullRequestStatusTool = new GitHubGetPullRequestStatusTool(fakeGitHubPullRequestStatusClient, gitHubSettings);
ToolResult pullRequestStatusResult = await getPullRequestStatusTool.ExecuteAsync("{\"number\":12}", CancellationToken.None);
AssertTrue(pullRequestStatusResult.Success, "GitHubGetPullRequestStatusTool succeeds for default allowed repo");
AssertTrue(pullRequestStatusResult.Output.Contains("#12 [open] Add GitHub PR listing"), "GitHubGetPullRequestStatusTool renders PR number, state, and title");
AssertTrue(pullRequestStatusResult.Output.Contains("Mergeable: true (clean)"), "GitHubGetPullRequestStatusTool renders mergeable status");
AssertTrue(pullRequestStatusResult.Output.Contains("Changes: +44 -3 across 6 files"), "GitHubGetPullRequestStatusTool renders change summary");
AssertTrue(pullRequestStatusResult.Output.Contains("Requested reviewers: reviewer1"), "GitHubGetPullRequestStatusTool renders requested reviewers");
AssertFalse(pullRequestStatusResult.Output.Contains("secret-token-for-test"), "GitHubGetPullRequestStatusTool never exposes token value");
AssertTrue(fakeGitHubPullRequestStatusHandler.LastRequestUri?.ToString().Contains("/repos/mujahedgt/TelegramMessagingTool/pulls/12") == true, "GitHubGetPullRequestStatusTool calls the PR detail endpoint");
ToolResult blockedPullRequestStatusResult = await getPullRequestStatusTool.ExecuteAsync("{\"owner\":\"other\",\"repo\":\"repo\",\"number\":12}", CancellationToken.None);
AssertFalse(blockedPullRequestStatusResult.Success, "GitHubGetPullRequestStatusTool rejects repositories outside allowlist");
ToolResult invalidPullRequestNumberResult = await getPullRequestStatusTool.ExecuteAsync("{\"number\":0}", CancellationToken.None);
AssertFalse(invalidPullRequestNumberResult.Success, "GitHubGetPullRequestStatusTool rejects invalid PR numbers");
ToolRegistry gitHubToolRegistry = ToolRegistryFactory.Create(new BotSettings(
    BotToken: "test-token",
    OllamaUrl: "http://localhost:11434/api/chat",
    OllamaModel: "qwen3:0.6b",
    OllamaEmbeddingUrl: "http://localhost:11434/api/embed",
    OllamaEmbeddingModel: "nomic-embed-text",
    EnableDocumentEmbeddings: false,
    EnableOnlineSearch: false,
    AdminChatId: 123456789,
    AllowedChatIds: new HashSet<long>(),
    AllowPublicAccess: false,
    DatabaseConnectionString: "test",
    ApplyMigrations: true,
    LogMessageContent: false,
    ConversationMaxHistory: 8,
    SearchRoutingMode: "heuristic",
    EnableSafeCommandTools: false,
    SafeCommandProjectRoot: Directory.GetCurrentDirectory(),
    EnablePlugins: false,
    PluginDirectory: Path.Combine(Directory.GetCurrentDirectory(), "plugins"))
{
    GitHub = gitHubSettings
}, fakeGitHubClient);
AssertTrue(gitHubToolRegistry.RenderToolInstructions().Contains("github_repo_info"), "ToolRegistryFactory registers GitHub repo info tool when enabled");
AssertTrue(gitHubToolRegistry.RenderToolInstructions().Contains("github_list_issues"), "ToolRegistryFactory registers GitHub list issues tool when enabled");
AssertTrue(gitHubToolRegistry.RenderToolInstructions().Contains("github_get_issue"), "ToolRegistryFactory registers GitHub get issue tool when enabled");
AssertTrue(gitHubToolRegistry.RenderToolInstructions().Contains("github_list_prs"), "ToolRegistryFactory registers GitHub list PRs tool when enabled");
AssertTrue(gitHubToolRegistry.RenderToolInstructions().Contains("github_get_pr_status"), "ToolRegistryFactory registers GitHub get PR status tool when enabled");
AssertFalse(gitHubToolRegistry.RenderToolInstructions().Contains("github_create_issue"), "ToolRegistryFactory excludes GitHub write tools unless write flag and pending service are enabled");
AssertTrue(CommandParser.TryParse("/status", out ParsedCommand parsedStatus), "CommandParser parses bare command");
AssertEqual("/status", parsedStatus.Command, "CommandParser normalizes bare command token");
AssertEqual(string.Empty, parsedStatus.Arguments, "CommandParser returns empty arguments for bare command");
AssertTrue(CommandParser.TryParse("/status@red_eye_ghost_bot detailed", out ParsedCommand parsedMentionCommand), "CommandParser parses command addressed to bot username");
AssertEqual("/status", parsedMentionCommand.Command, "CommandParser strips bot username suffix");
AssertEqual("red_eye_ghost_bot", parsedMentionCommand.BotUsername, "CommandParser captures bot username suffix");
AssertEqual("detailed", parsedMentionCommand.Arguments, "CommandParser extracts arguments after mention command");
AssertFalse(CommandParser.Matches("/statusx", "/status"), "CommandParser does not match command prefixes");
AssertFalse(CommandParser.Matches("/status-extra", "/status"), "CommandParser does not match command names with extra suffixes");
AssertTrue(CommandParser.Matches("/status@red_eye_ghost_bot", "/status"), "CommandParser matches bot-addressed commands");
AssertEqual("hello world", CommandParser.GetArguments("/remember@red_eye_ghost_bot hello world", "/remember"), "CommandParser extracts arguments from bot-addressed command");
AssertTrue(TelegramReactionService.IsSupportedReactionEmoji("✅"), "TelegramReactionService accepts configured success reaction");
AssertTrue(TelegramReactionService.IsSupportedReactionEmoji("🧹"), "TelegramReactionService accepts configured reset reaction");
AssertFalse(TelegramReactionService.IsSupportedReactionEmoji("not-an-emoji"), "TelegramReactionService rejects unsupported reaction metadata");

string validOllamaJson = """
{
  "message": {
    "role": "assistant",
    "content": "Hello from Ollama"
  }
}
""";
AssertEqual("Hello from Ollama", OllamaChatClient.ParseAssistantContent(validOllamaJson), "ParseAssistantContent reads assistant content");
AssertEqual("Invalid response received from Ollama.", OllamaChatClient.ParseAssistantContent("not json"), "ParseAssistantContent handles invalid JSON");

string promptWithMemory = ConversationService.BuildSystemPrompt([
    new Memory { Content = "User is learning C#." }
]);
AssertTrue(promptWithMemory.Contains("Known memories about this user:"), "BuildSystemPrompt includes memory heading");
AssertTrue(promptWithMemory.Contains("User is learning C#."), "BuildSystemPrompt includes memory content");
AssertTrue(promptWithMemory.Contains("Available Telegram commands"), "BuildSystemPrompt documents commands");
AssertTrue(promptWithMemory.Contains("live web search is disabled"), "BuildSystemPrompt tells model not to guess when search is unavailable");
AssertFalse(promptWithMemory.Contains("request the `online_search` tool"), "BuildSystemPrompt does not advertise online_search without tool instructions");
string promptWithSearchInstructions = ConversationService.BuildSystemPrompt([], "Available tools:\n- online_search: Search web");
AssertTrue(promptWithSearchInstructions.Contains("online_search"), "BuildSystemPrompt includes online_search only from active tool instructions");
AssertFalse(promptWithMemory.Contains("unless a future tool system actually provides"), "BuildSystemPrompt does not mention unavailable future tool-calling");

ToolCallParseResult noToolCall = ToolCallParser.Parse("Normal assistant response");
AssertFalse(noToolCall.IsToolCall, "ToolCallParser ignores normal text");

ToolCallParseResult parsedToolCall = ToolCallParser.Parse("{\"type\":\"tool_call\",\"tool\":\"calculator\",\"input\":\"25*19\"}");
AssertTrue(parsedToolCall.IsToolCall, "ToolCallParser accepts strict tool call JSON");
AssertEqual("calculator", parsedToolCall.ToolName, "ToolCallParser extracts tool name");
AssertEqual("25*19", parsedToolCall.Input, "ToolCallParser extracts tool input");
ToolCallParseResult embeddedToolCall = ToolCallParser.Parse("I will search now:\n{\"type\":\"tool_call\",\"tool\":\"online_search\",\"input\":\"Mitsubishi Lancer 1992 price specs\"}");
AssertTrue(embeddedToolCall.IsToolCall, "ToolCallParser extracts embedded tool call JSON from chatty model output");
AssertEqual("online_search", embeddedToolCall.ToolName, "ToolCallParser extracts embedded tool name");

var heuristicSearchRoutingClassifier = new HeuristicSearchRoutingClassifier();
SearchRoutingDecision newestMitsubishiDecision = heuristicSearchRoutingClassifier.Classify([new OllamaMessageDto("user", "what is the newest car from mitsubishi")]);
AssertTrue(newestMitsubishiDecision.ShouldSearch, "HeuristicSearchRoutingClassifier directly searches newest/current factual questions");
AssertTrue(newestMitsubishiDecision.Query.Contains("Mitsubishi", StringComparison.OrdinalIgnoreCase), "HeuristicSearchRoutingClassifier keeps the requested brand in direct search query");
AssertTrue(newestMitsubishiDecision.Query.Contains("official", StringComparison.OrdinalIgnoreCase), "HeuristicSearchRoutingClassifier expands newest/current direct search query with official/latest context");
AssertTrue(
    AgentRunner.TryBuildDirectSearchQuery([new OllamaMessageDto("user", "what is the newest car from mitsubishi")], out string newestMitsubishiQuery),
    "AgentRunner compatibility helper delegates direct search classification");
AssertEqual(newestMitsubishiDecision.Query, newestMitsubishiQuery, "AgentRunner compatibility helper preserves heuristic search query behavior");
AssertFalse(
    heuristicSearchRoutingClassifier.Classify([new OllamaMessageDto("user", "explain delegates in C#")]).ShouldSearch,
    "HeuristicSearchRoutingClassifier skips non-current local/explanation questions");
ISearchRoutingClassifier offSearchRoutingClassifier = SearchRoutingClassifierFactory.Create("off");
SearchRoutingDecision offSearchDecision = await offSearchRoutingClassifier.ClassifyAsync(
    [new OllamaMessageDto("user", "what is the newest car from mitsubishi")],
    CancellationToken.None);
AssertFalse(offSearchDecision.ShouldSearch, "SearchRoutingClassifierFactory creates an off classifier that disables direct online search");
AssertTrue(SearchRoutingClassifierFactory.Create("heuristic") is HeuristicSearchRoutingClassifier, "SearchRoutingClassifierFactory creates heuristic classifier");
AssertTrue(SearchRoutingClassifierFactory.Create("invalid") is HeuristicSearchRoutingClassifier, "SearchRoutingClassifierFactory falls back to heuristic classifier");
var llmRoutingClient = new ScriptedChatClient([
    "{\"should_search\":true,\"query\":\"latest .NET version\",\"reason\":\"needs current external facts\",\"confidence\":0.92}"
]);
ISearchRoutingClassifier llmSearchRoutingClassifier = SearchRoutingClassifierFactory.Create("llm", llmRoutingClient);
AssertTrue(llmSearchRoutingClassifier is LlmSearchRoutingClassifier, "SearchRoutingClassifierFactory creates LLM classifier when chat client is provided");
SearchRoutingDecision llmSearchDecision = await llmSearchRoutingClassifier.ClassifyAsync(
    [new OllamaMessageDto("user", "what is the latest .NET version")],
    CancellationToken.None);
AssertTrue(llmSearchDecision.ShouldSearch, "LlmSearchRoutingClassifier accepts should_search=true JSON");
AssertEqual("latest .NET version", llmSearchDecision.Query, "LlmSearchRoutingClassifier parses query");
AssertEqual("needs current external facts", llmSearchDecision.Reason, "LlmSearchRoutingClassifier parses reason");
AssertEqual(ModelTaskKind.Chat, llmRoutingClient.ModelTaskKinds.Single(), "LlmSearchRoutingClassifier uses chat route for classification");
var invalidLlmRoutingClient = new ScriptedChatClient(["not json"]);
SearchRoutingDecision invalidLlmSearchDecision = await new LlmSearchRoutingClassifier(invalidLlmRoutingClient).ClassifyAsync(
    [new OllamaMessageDto("user", "latest phone price")],
    CancellationToken.None);
AssertFalse(invalidLlmSearchDecision.ShouldSearch, "LlmSearchRoutingClassifier falls back to no-search on invalid JSON");

var calculator = new CalculatorTool();
ToolResult calculation = await calculator.ExecuteAsync("25 * 19", CancellationToken.None);
AssertTrue(calculation.Success, "CalculatorTool accepts safe math");
AssertTrue(calculation.Output.Contains("475"), "CalculatorTool calculates multiplication");
ToolResult rejectedCalculation = await calculator.ExecuteAsync("System.IO.File.Delete('x')", CancellationToken.None);
AssertFalse(rejectedCalculation.Success, "CalculatorTool rejects non-math input");

var dateTimeTool = new DateTimeTool();
ToolResult dateTimeResult = await dateTimeTool.ExecuteAsync("", CancellationToken.None);
AssertTrue(dateTimeResult.Success, "DateTimeTool succeeds");
AssertTrue(dateTimeResult.Output.Contains("UTC"), "DateTimeTool reports UTC time");

string observationPrompt = AgentRunner.BuildToolObservationPrompt("calculator", ToolResult.Ok("475"), 1, 3);
AssertTrue(observationPrompt.Contains("Tool observation 1/3"), "AgentRunner labels tool observations with step count");
AssertTrue(observationPrompt.Contains("one more strict tool_call"), "AgentRunner allows another tool before the limit");
string finalObservationPrompt = AgentRunner.BuildToolObservationPrompt("datetime", ToolResult.Ok("UTC now"), 3, 3);
AssertTrue(finalObservationPrompt.Contains("Do not request another tool"), "AgentRunner blocks further tools at the step limit");

var scriptedChatClient = new ScriptedChatClient([
    "{\"type\":\"tool_call\",\"tool\":\"calculator\",\"input\":\"25 * 19\"}",
    "{\"type\":\"tool_call\",\"tool\":\"datetime\",\"input\":\"\"}",
    "The calculation is 475, and I also checked the current time."
]);
var multiStepRunner = new AgentRunner(scriptedChatClient, new ToolRegistry([calculator, dateTimeTool]), maxToolIterations: 3);
string multiStepAnswer = await multiStepRunner.RunAsync([new OllamaMessageDto("user", "Calculate 25 * 19 and then check the time.")], CancellationToken.None);
AssertTrue(multiStepAnswer.Contains("475"), "AgentRunner returns final answer after multiple tool observations");
AssertEqual(3, scriptedChatClient.Calls, "AgentRunner asks model again after each safe tool observation until final answer");
AssertTrue(scriptedChatClient.ModelTaskKinds.All(x => x == ModelTaskKind.Chat), "AgentRunner uses chat model route for normal/tool-loop chat calls");

var searchFinalChatClient = new ScriptedChatClient(["Search-grounded final answer"]);
var searchFinalRunner = new AgentRunner(searchFinalChatClient, new ToolRegistry([new FakeSearchTool()]), maxToolIterations: 3);
string searchFinalAnswer = await searchFinalRunner.RunAsync([new OllamaMessageDto("user", "what is the newest mitsubishi")], CancellationToken.None);
AssertEqual("Search-grounded final answer", searchFinalAnswer, "AgentRunner returns final search synthesis answer");
AssertEqual(ModelTaskKind.ToolFinalAnswer, searchFinalChatClient.ModelTaskKinds.Single(), "AgentRunner uses tool-final model route for online-search final synthesis");

AgentBehaviorEvalReport behaviorEvalReport = await AgentBehaviorEvalSuite.RunAsync(CancellationToken.None);
AssertTrue(behaviorEvalReport.Passed, "Agent behavior eval suite passes all scripted scenarios");
AssertTrue(behaviorEvalReport.ContainsPassed("model_tool_call_json_is_executed"), "Agent behavior eval covers model-emitted tool_call JSON execution");
AssertTrue(behaviorEvalReport.ContainsPassed("approval_tool_creates_pending_action"), "Agent behavior eval covers approval tool pending-action request behavior");
AssertTrue(behaviorEvalReport.ContainsPassed("failed_tool_result_is_explained_safely"), "Agent behavior eval covers safe explanation after failed tool result");
AssertTrue(behaviorEvalReport.ContainsPassed("search_routing_avoids_false_positive"), "Agent behavior eval covers false-positive search routing avoidance");

CommandResult helpDocsResult = await new HelpCommand().TryHandleAsync(TextMessage("/help"), new ConnectedUser { ChatId = 1, Name = "docs" }, null!, CancellationToken.None);
AssertTrue(helpDocsResult.Handled, "/help docs sync check is handled");
AssertFalse(helpDocsResult.ReplyText?.Contains("vision description/OCR is planned next", StringComparison.OrdinalIgnoreCase) == true, "/help does not describe implemented image vision as fully planned");
AssertFalse(helpDocsResult.ReplyText?.Contains("planned image-agent", StringComparison.OrdinalIgnoreCase) == true, "/help no longer describes image commands as only planned");
AssertFalse(helpDocsResult.ReplyText?.Contains("planned voice-agent", StringComparison.OrdinalIgnoreCase) == true, "/help no longer describes voice commands as only planned");
AssertFalse(helpDocsResult.ReplyText?.Contains("read-only plugin manifest discovery", StringComparison.OrdinalIgnoreCase) == true, "/help no longer describes plugins as manifest-only after trusted loading exists");
string harnessCatalogSource = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "TelegramMessagingTool", "Services", "AgentHarnessCatalog.cs"), CancellationToken.None);
AssertFalse(harnessCatalogSource.Contains("These are planning harnesses only", StringComparison.OrdinalIgnoreCase), "/harnesses output no longer says implemented media gates do not execute");
AssertFalse(harnessCatalogSource.Contains("Status: \"planned\"", StringComparison.OrdinalIgnoreCase), "Harness catalog no longer marks implemented image/voice foundations as planned-only");
string imagesCommandSource = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "TelegramMessagingTool", "Commands", "ImagesCommand.cs"), CancellationToken.None);
AssertFalse(imagesCommandSource.Contains("not implemented yet", StringComparison.OrdinalIgnoreCase), "/images no longer claims /describeimage is not implemented");
string voiceFilesCommandSource = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "TelegramMessagingTool", "Commands", "VoiceFilesCommand.cs"), CancellationToken.None);
AssertFalse(voiceFilesCommandSource.Contains("not implemented yet", StringComparison.OrdinalIgnoreCase), "/voicefiles no longer claims transcription/insights are not implemented");
string readmeDocs = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "README.md"), CancellationToken.None);
AssertFalse(readmeDocs.Contains("does not load plugin assemblies", StringComparison.OrdinalIgnoreCase), "README no longer says plugins only scan manifests after trusted loading exists");
AssertFalse(readmeDocs.Contains("planning only", StringComparison.OrdinalIgnoreCase), "README no longer describes image/voice harnesses as planning-only after vision/audio/TTS gates exist");
AssertTrue(readmeDocs.Contains("scripted agent behavior evals", StringComparison.OrdinalIgnoreCase), "README documents scripted behavior eval coverage");
AssertTrue(readmeDocs.Contains("/riskconfig", StringComparison.OrdinalIgnoreCase), "README documents /riskconfig command");

DateTime scheduleNow = new(2026, 6, 26, 12, 0, 0, DateTimeKind.Utc);
AssertTrue(ScheduleParser.TryParse("2026-06-28 18:30", scheduleNow, out ScheduleParseResult absoluteSchedule), "ScheduleParser parses yyyy-MM-dd HH:mm");
AssertEqual(new DateTime(2026, 6, 28, 18, 30, 0, DateTimeKind.Utc), absoluteSchedule.ScheduledAtUtc, "ScheduleParser returns UTC absolute schedule time");
AssertEqual("2026-06-28 18:30 UTC", absoluteSchedule.DisplayText, "ScheduleParser renders absolute schedule display");

AssertTrue(ScheduleParser.TryParse("tomorrow 09:15", scheduleNow, out ScheduleParseResult tomorrowSchedule), "ScheduleParser parses tomorrow HH:mm");
AssertEqual(new DateTime(2026, 6, 27, 9, 15, 0, DateTimeKind.Utc), tomorrowSchedule.ScheduledAtUtc, "ScheduleParser schedules tomorrow at requested UTC time");

AssertTrue(ScheduleParser.TryParse("in 30m", scheduleNow, out ScheduleParseResult inMinutesSchedule), "ScheduleParser parses in Nm");
AssertEqual(scheduleNow.AddMinutes(30), inMinutesSchedule.ScheduledAtUtc, "ScheduleParser schedules relative minutes");

AssertTrue(ScheduleParser.TryParse("in 2h", scheduleNow, out ScheduleParseResult inHoursSchedule), "ScheduleParser parses in Nh");
AssertEqual(scheduleNow.AddHours(2), inHoursSchedule.ScheduledAtUtc, "ScheduleParser schedules relative hours");

AssertFalse(ScheduleParser.TryParse("yesterday 09:00", scheduleNow, out _), "ScheduleParser rejects unsupported natural language");
AssertFalse(ScheduleParser.TryParse("in 0m", scheduleNow, out _), "ScheduleParser rejects zero delay");
AssertFalse(ScheduleParser.TryParse("2026-06-26 11:59", scheduleNow, out _), "ScheduleParser rejects past absolute time");

var searchDisabledSettings = new BotSettings(
    BotToken: "test-token",
    OllamaUrl: "http://localhost:11434/api/chat",
    OllamaModel: "llama3.2:3b",
    OllamaEmbeddingUrl: "http://localhost:11434/api/embed",
    OllamaEmbeddingModel: "nomic-embed-text",
    EnableDocumentEmbeddings: false,
    EnableOnlineSearch: false,
    AdminChatId: 0,
    AllowedChatIds: new HashSet<long>(),
    AllowPublicAccess: false,
    DatabaseConnectionString: "test-db",
    ApplyMigrations: true,
    LogMessageContent: false,
    ConversationMaxHistory: 8,
    SearchRoutingMode: "heuristic",
    EnableSafeCommandTools: false,
    SafeCommandProjectRoot: Directory.GetCurrentDirectory(),
    EnablePlugins: false,
    PluginDirectory: Path.Combine(Directory.GetCurrentDirectory(), "plugins"));
var searchEnabledSettings = searchDisabledSettings with { EnableOnlineSearch = true };

var defaultModelRouting = new ModelRoutingService(searchDisabledSettings);
AssertEqual("llama3.2:3b", defaultModelRouting.GetModel(ModelTaskKind.Chat), "ModelRoutingService defaults chat route to OLLAMA_MODEL");
AssertEqual("llama3.2:3b", defaultModelRouting.GetModel(ModelTaskKind.Planning), "ModelRoutingService defaults planning route to OLLAMA_MODEL");
AssertEqual("llama3.2:3b", defaultModelRouting.GetModel(ModelTaskKind.DocumentQuestionAnswering), "ModelRoutingService defaults document QA route to OLLAMA_MODEL");
AssertTrue(defaultModelRouting.RenderSummary().Contains("chat=llama3.2:3b"), "ModelRoutingService summary includes chat route");

var routedModelSettings = searchDisabledSettings with
{
    OllamaPlanningModel = "qwen3:4b",
    OllamaDocumentQuestionAnsweringModel = "qwen3:8b",
    OllamaDocumentSummaryModel = "qwen3:8b",
    OllamaToolFinalModel = "qwen3:0.6b-fast",
    OllamaImageModel = "llava:latest",
    OllamaVoiceModel = "qwen3:4b-voice"
};
var routedModelService = new ModelRoutingService(routedModelSettings);
AssertEqual("llama3.2:3b", routedModelService.GetModel(ModelTaskKind.Chat), "ModelRoutingService keeps chat on base model when chat override is blank");
AssertEqual("qwen3:4b", routedModelService.GetModel(ModelTaskKind.Planning), "ModelRoutingService uses planning override");
AssertEqual("qwen3:8b", routedModelService.GetModel(ModelTaskKind.DocumentQuestionAnswering), "ModelRoutingService uses document QA override");
AssertEqual("qwen3:8b", routedModelService.GetModel(ModelTaskKind.DocumentSummary), "ModelRoutingService uses document summary override");
AssertEqual("qwen3:0.6b-fast", routedModelService.GetModel(ModelTaskKind.ToolFinalAnswer), "ModelRoutingService uses tool final-answer override");
AssertEqual("llava:latest", routedModelService.GetModel(ModelTaskKind.Image), "ModelRoutingService uses image override");
AssertEqual("qwen3:4b-voice", routedModelService.GetModel(ModelTaskKind.Voice), "ModelRoutingService uses voice override");
AssertTrue(routedModelService.RenderSummary().Contains("plan=qwen3:4b"), "ModelRoutingService summary includes planning route");

var registry = new ToolRegistry([
    dateTimeTool,
    calculator,
    new OnlineSearchTool(new HttpClient()),
    new BotStatusTool(searchEnabledSettings)
]);
ToolRegistry privacyGatedRegistry = ToolRegistryFactory.Create(searchDisabledSettings, new HttpClient());
AssertFalse(privacyGatedRegistry.TryGet("online_search", out _), "ToolRegistryFactory excludes online_search by default");
AssertFalse(privacyGatedRegistry.TryGet("git_status", out _), "ToolRegistryFactory excludes safe command tools by default");
AssertFalse(privacyGatedRegistry.RenderToolInstructions().Contains("git_status"), "Disabled safe command tools are not advertised in model instructions");
ToolRegistry safeCommandRegistry = ToolRegistryFactory.Create(searchDisabledSettings with
{
    EnableSafeCommandTools = true,
    SafeCommandProjectRoot = Directory.GetCurrentDirectory()
}, new HttpClient());
AssertTrue(safeCommandRegistry.TryGet("git_status", out IAgentTool? gitStatusTool), "ToolRegistryFactory includes git_status when safe command tools are enabled");
AssertTrue(safeCommandRegistry.TryGet("git_diff", out _), "ToolRegistryFactory includes git_diff when safe command tools are enabled");
AssertTrue(safeCommandRegistry.TryGet("git_log_recent", out _), "ToolRegistryFactory includes git_log_recent when safe command tools are enabled");
AssertTrue(safeCommandRegistry.TryGet("run_dotnet_tests", out IAgentTool? runDotnetTestsTool), "ToolRegistryFactory includes run_dotnet_tests when safe command tools are enabled");
AssertFalse(gitStatusTool!.RequiresApproval, "git_status is read-only and does not require approval");
AssertFalse(runDotnetTestsTool!.RequiresApproval, "run_dotnet_tests uses a fixed bounded command and does not require approval");
AssertTrue(safeCommandRegistry.RenderToolInstructions().Contains("{\"target\":\"helper-tests\"}"), "ToolRegistry instructions document run_dotnet_tests strict JSON input");
ToolResult gitStatusResult = await gitStatusTool.ExecuteAsync(string.Empty, CancellationToken.None);
AssertTrue(gitStatusResult.Success, "git_status runs successfully in the project root: " + gitStatusResult.Output);
AssertTrue(gitStatusResult.Output.Contains("git status", StringComparison.OrdinalIgnoreCase), "git_status output identifies the command");
ToolResult invalidTestTargetResult = await runDotnetTestsTool.ExecuteAsync("{\"target\":\"all\"}", CancellationToken.None);
AssertFalse(invalidTestTargetResult.Success, "run_dotnet_tests rejects unsupported test targets");
AssertTrue(invalidTestTargetResult.Output.Contains("helper-tests", StringComparison.OrdinalIgnoreCase), "run_dotnet_tests rejection explains the allowed target");
    ToolResult malformedTestTargetResult = await runDotnetTestsTool.ExecuteAsync("helper-tests", CancellationToken.None);
AssertFalse(malformedTestTargetResult.Success, "run_dotnet_tests rejects non-JSON input");
ToolRegistry repoWriteDisabledRegistry = ToolRegistryFactory.Create(searchDisabledSettings, new HttpClient(), new PendingActionService());
AssertFalse(repoWriteDisabledRegistry.TryGet("repo_replace_text", out _), "ToolRegistryFactory excludes repo write tools by default");
ToolRegistry repoWriteEnabledRegistry = ToolRegistryFactory.Create(searchDisabledSettings with
{
    EnableRepoWriteTools = true,
    SafeCommandProjectRoot = Directory.GetCurrentDirectory()
}, new HttpClient(), new PendingActionService());
AssertTrue(repoWriteEnabledRegistry.TryGet("repo_replace_text", out IAgentTool? repoReplaceTextTool), "ToolRegistryFactory includes repo_replace_text when repo write tools are enabled");
AssertTrue(repoWriteEnabledRegistry.TryGet("repo_commit_changes", out IAgentTool? repoCommitChangesTool), "ToolRegistryFactory includes repo_commit_changes when repo write tools are enabled");
AssertTrue(repoWriteEnabledRegistry.TryGet("repo_push_changes", out IAgentTool? repoPushChangesTool), "ToolRegistryFactory includes repo_push_changes when repo write tools are enabled");
AssertTrue(repoReplaceTextTool!.RequiresApproval, "repo_replace_text requires approval before editing files");
AssertTrue(repoCommitChangesTool!.RequiresApproval, "repo_commit_changes requires approval before committing changes");
AssertTrue(repoPushChangesTool!.RequiresApproval, "repo_push_changes requires approval before pushing changes");
AssertTrue(repoWriteEnabledRegistry.RenderToolInstructions().Contains("repo_replace_text"), "ToolRegistry instructions document repo_replace_text when enabled");
AssertTrue(repoWriteEnabledRegistry.RenderToolInstructions().Contains("repo_commit_changes"), "ToolRegistry instructions document repo_commit_changes when enabled");
AssertTrue(repoWriteEnabledRegistry.RenderToolInstructions().Contains("repo_push_changes"), "ToolRegistry instructions document repo_push_changes when enabled");
ToolRegistry enabledSearchRegistry = ToolRegistryFactory.Create(searchEnabledSettings, new HttpClient());
AssertTrue(enabledSearchRegistry.TryGet("online_search", out _), "ToolRegistryFactory includes online_search only when enabled");
AssertTrue(enabledSearchRegistry.RenderToolInstructions().Contains("online_search"), "Enabled online_search is advertised in model instructions");
AssertFalse(registry.RenderToolInstructions().Contains("Mitsubateie", StringComparison.OrdinalIgnoreCase), "ToolRegistry online_search instructions avoid domain-specific typo examples");
AssertFalse(registry.RenderToolInstructions().Contains("Mitsubishi Lancer", StringComparison.OrdinalIgnoreCase), "ToolRegistry online_search instructions avoid hardcoded correction examples");
AssertTrue(registry.TryGet("calculator", out IAgentTool? registeredCalculator), "ToolRegistry finds calculator");
AssertEqual("calculator", registeredCalculator!.Name, "ToolRegistry returns matching tool");
AssertTrue(registry.RenderToolList().Contains("online_search"), "ToolRegistry lists online search");

IReadOnlyList<AgentHarnessDefinition> harnesses = AgentHarnessCatalog.GetDefaultHarnesses();
AssertEqual(2, harnesses.Count, "AgentHarnessCatalog defines image and voice harnesses");
AssertTrue(harnesses.Any(x => x.Name == "image_agent"), "AgentHarnessCatalog includes image agent harness");
AssertTrue(harnesses.Any(x => x.Name == "voice_agent"), "AgentHarnessCatalog includes voice agent harness");
AssertTrue(harnesses.Single(x => x.Name == "image_agent").ImplementedGates.Any(x => x.Contains("/describeimage", StringComparison.OrdinalIgnoreCase)), "Image harness lists implemented describeimage gate");
AssertTrue(harnesses.Single(x => x.Name == "voice_agent").ImplementedGates.Any(x => x.Contains("/transcribe", StringComparison.OrdinalIgnoreCase)), "Voice harness lists implemented transcribe gate");
string renderedHarnesses = AgentHarnessCatalog.RenderHarnesses(harnesses);
AssertTrue(renderedHarnesses.Contains("Image and Voice Agent Harnesses"), "AgentHarnessCatalog renders the current harness title");
AssertTrue(renderedHarnesses.Contains("image_agent"), "AgentHarnessCatalog render includes image harness");
AssertTrue(renderedHarnesses.Contains("voice_agent"), "AgentHarnessCatalog render includes voice harness");
string renderedHarnessesWithRoutes = AgentHarnessCatalog.RenderHarnesses(searchDisabledSettings, harnesses);
AssertTrue(renderedHarnessesWithRoutes.Contains("Harness model routes"), "AgentHarnessCatalog renders harness model routes when settings are provided");
AssertTrue(renderedHarnessesWithRoutes.Contains("image_agent route"), "AgentHarnessCatalog shows image route model");
var harnessesCommand = new HarnessesCommand(searchDisabledSettings);
CommandResult harnessesCommandResult = await harnessesCommand.TryHandleAsync(
    new Message { Text = "/harnesses" },
    new ConnectedUser { ChatId = 123, FirstName = "Test" },
    null!,
    CancellationToken.None);
AssertTrue(harnessesCommandResult.Handled, "HarnessesCommand handles /harnesses");
AssertTrue(harnessesCommandResult.ReplyText?.Contains("image_agent") == true, "HarnessesCommand reply includes image harness");
AssertTrue(harnessesCommandResult.ReplyText?.Contains("voice_agent") == true, "HarnessesCommand reply includes voice harness");
AssertFalse((await harnessesCommand.TryHandleAsync(new Message { Text = "/harnessesx" }, new ConnectedUser { ChatId = 123 }, null!, CancellationToken.None)).Handled, "HarnessesCommand uses exact command matching");

Uri searchUri = OnlineSearchTool.BuildSearchUri("asp.net core performance tips");
AssertTrue(searchUri.ToString().Contains("startpage.com"), "OnlineSearchTool primary endpoint uses Startpage compatibility helper");
AssertTrue(searchUri.Query.Contains("asp.net"), "OnlineSearchTool includes query text");
IReadOnlyList<string> typoSearchVariants = OnlineSearchTool.BuildSearchQueryVariants("Mitsubateie Lanser 1992");
AssertTrue(typoSearchVariants.Contains("Mitsubateie Lanser 1992"), "OnlineSearchTool preserves the user's original query instead of applying domain-specific typo corrections");
AssertFalse(typoSearchVariants.Any(x => x.Contains("Mitsubishi Lancer", StringComparison.OrdinalIgnoreCase)), "OnlineSearchTool does not hardcode Mitsubishi/Lancer typo correction");
AssertTrue(typoSearchVariants.Any(x => x.Contains("price", StringComparison.OrdinalIgnoreCase) && x.Contains("spec", StringComparison.OrdinalIgnoreCase)), "OnlineSearchTool still expands year-based vehicle searches with price/spec terms");

string startPageFixture = """
<a class="result-title result-link" href="https://www.microsoft.com/en-us/surface/devices/surface-laptop"><h2>Surface Laptop 8th Edition - Microsoft</h2></a>
<p class="description">Processor. 8-core Snapdragon X Plus. Battery life. Up to 23 hours.</p>
""";
IReadOnlyList<SearchResult> parsedSearchResults = OnlineSearchTool.ParseSearchHtml(startPageFixture);
AssertEqual(1, parsedSearchResults.Count, "OnlineSearchTool parses Startpage-style results");
AssertTrue(parsedSearchResults[0].Title.Contains("Surface Laptop 8th"), "OnlineSearchTool parses result title");
AssertTrue(parsedSearchResults[0].Snippet.Contains("Snapdragon"), "OnlineSearchTool parses result snippet");
string readablePageText = OnlineSearchTool.ExtractReadablePageText("<html><head><script>ignore()</script></head><body><h1>New Mitsubishi</h1><p>Official latest model details.</p></body></html>");
AssertTrue(readablePageText.Contains("New Mitsubishi"), "OnlineSearchTool extracts readable page text");
AssertFalse(readablePageText.Contains("ignore()"), "OnlineSearchTool removes script content from page extracts");
string renderedSearchWithExtract = OnlineSearchTool.RenderResults(
    "newest mitsubishi",
    "newest mitsubishi 2026 official latest model",
    [new SearchResult("Mitsubishi Motors: What's new for 2026", "https://example.com/mitsubishi", "Official 2026 lineup")],
    "fixture",
    [new PageExtract("Mitsubishi Motors: What's new for 2026", "https://example.com/mitsubishi", "The 2026 lineup includes updated Outlander details.")]);
AssertTrue(renderedSearchWithExtract.Contains("Read page extracts"), "OnlineSearchTool renders read page extracts");
AssertTrue(renderedSearchWithExtract.Contains("updated Outlander"), "OnlineSearchTool includes page extract text in tool output");

string consolePanel = AgentConsoleRenderer.RenderStartupPanel(new AgentConsoleSnapshot(
    BotUsername: "test_bot",
    OllamaUrl: "http://localhost:11434/api/chat",
    OllamaModel: "llama3.2:3b",
    DatabaseConnection: "LocalDB",
    AccessMode: "public override",
    MessageContentLoggingEnabled: false,
    OnlineSearchEnabled: true,
    ApplyMigrations: true,
    Commands: ["/help", "/status", "/tools"],
    Tools: registry.Tools.Select(x => x.Name).ToList()));
AssertTrue(consolePanel.Contains("TelegramMessagingTool Agent Console"), "Console renderer has title");
AssertTrue(consolePanel.Contains("/tools"), "Console renderer lists commands");
AssertTrue(consolePanel.Contains("online_search"), "Console renderer lists tools");
AssertTrue(consolePanel.Contains("Quick start"), "Console renderer shows quick start guidance");
AssertTrue(consolePanel.Contains("Type directly in this console"), "Console renderer shows console and Telegram usage examples");
AssertTrue(consolePanel.Contains("/exit"), "Console renderer documents console exit command");
AssertTrue(consolePanel.Contains("Runtime risk warnings"), "Console renderer shows runtime risk warning section");
AssertTrue(consolePanel.Contains("ALLOW_PUBLIC_ACCESS is enabled"), "Console renderer warns when public access override is enabled");
AssertTrue(consolePanel.Contains("Anyone who finds the bot can use it"), "Console renderer explains public override risk");

string lockedConsolePanel = AgentConsoleRenderer.RenderStartupPanel(new AgentConsoleSnapshot(
    BotUsername: "test_bot",
    OllamaUrl: "http://localhost:11434/api/chat",
    OllamaModel: "llama3.2:3b",
    DatabaseConnection: "LocalDB",
    AccessMode: "locked",
    MessageContentLoggingEnabled: false,
    OnlineSearchEnabled: false,
    ApplyMigrations: true,
    Commands: ["/help"],
    Tools: ["calculator"]));
AssertTrue(lockedConsolePanel.Contains("Telegram access is locked"), "Console renderer warns when bot is locked");
AssertTrue(lockedConsolePanel.Contains("Online search disabled"), "Console renderer shows online search disabled quick-start note");
AssertFalse(lockedConsolePanel.Contains("online_search"), "Console renderer does not list online_search when disabled");

string adminOnlyConsolePanel = AgentConsoleRenderer.RenderStartupPanel(new AgentConsoleSnapshot(
    BotUsername: "test_bot",
    OllamaUrl: "http://localhost:11434/api/chat",
    OllamaModel: "llama3.2:3b",
    DatabaseConnection: "LocalDB",
    AccessMode: "admin-only",
    MessageContentLoggingEnabled: false,
    OnlineSearchEnabled: false,
    ApplyMigrations: true,
    Commands: ["/help"],
    Tools: ["calculator"]));
AssertTrue(adminOnlyConsolePanel.Contains("No immediate runtime risk warnings."), "Console renderer does not warn for admin-only mode");
AssertFalse(adminOnlyConsolePanel.Contains("Anyone who finds the bot can use it"), "Console renderer does not show public warning for admin-only mode");

string maskedConnection = AgentConsoleRenderer.SummarizeDatabaseConnection("Server=(localdb)\\MSSQLLocalDB;Database=TelegramMessagingTool;User Id=admin;Password=secret-password;TrustServerCertificate=True");
AssertTrue(maskedConnection.Contains("Database=TelegramMessagingTool"), "Database connection summary keeps useful database name");
AssertFalse(maskedConnection.Contains("secret-password"), "Database connection summary masks password");
AssertFalse(maskedConnection.Contains("User Id=admin"), "Database connection summary hides user id");

string eventLine = AgentConsoleRenderer.RenderEvent("MESSAGE", "tester", "handled with tools", ConsoleEventLevel.Success);
AssertTrue(eventLine.Contains("MESSAGE"), "Console event includes label");
AssertTrue(eventLine.Contains("tester"), "Console event includes actor");
AssertTrue(eventLine.Contains("handled with tools"), "Console event includes detail");

string dashboard = AgentConsoleRenderer.RenderDashboard(new RuntimeDashboardSnapshot(
    ActiveTasks: 3,
    PendingApprovals: 1,
    IndexedDocs: 8,
    SavedFiles: 9,
    SavedImages: 4,
    RecentWarnings: 2,
    Uptime: TimeSpan.FromMinutes(5),
    AccessMode: "admin-only",
    DatabaseConnection: "Server=(localdb)\\MSSQLLocalDB;Database=TelegramMessagingTool;User Id=admin;Password=secret-password"));
AssertTrue(dashboard.Contains("Runtime dashboard", StringComparison.OrdinalIgnoreCase), "Console dashboard has a title");
AssertTrue(dashboard.Contains("Active tasks") && dashboard.Contains("3"), "Console dashboard shows active task count");
AssertTrue(dashboard.Contains("Pending approvals") && dashboard.Contains("1"), "Console dashboard shows pending approval count");
AssertTrue(dashboard.Contains("Indexed docs") && dashboard.Contains("8"), "Console dashboard shows indexed document count");
AssertTrue(dashboard.Contains("Saved images") && dashboard.Contains("4"), "Console dashboard shows saved image count");
AssertTrue(dashboard.Contains("START, MESSAGE, COMMAND, TOOL, DOCUMENT, IMAGE, TASK, APPROVAL, ERROR, NET"), "Console dashboard lists event categories");
AssertFalse(dashboard.Contains("secret-password", StringComparison.OrdinalIgnoreCase), "Console dashboard masks database passwords");
AssertFalse(dashboard.Contains("User Id=admin", StringComparison.OrdinalIgnoreCase), "Console dashboard hides database user ids");

string eventLog = AgentConsoleRenderer.RenderEventLog([
    new RuntimeEventEntry(DateTimeOffset.Parse("2026-07-08T00:00:00Z"), ConsoleEventLevel.Warning, "NET", "provider TOKEN=[REDACTED] failed"),
    new RuntimeEventEntry(DateTimeOffset.Parse("2026-07-08T00:01:00Z"), ConsoleEventLevel.Error, "ERROR", "socket reset")
]);
AssertTrue(eventLog.Contains("Recent runtime events", StringComparison.OrdinalIgnoreCase), "Console event log has a title");
AssertTrue(eventLog.Contains("WARN") && eventLog.Contains("NET"), "Console event log renders warning events");
AssertTrue(eventLog.Contains("ERR") && eventLog.Contains("ERROR"), "Console event log renders error events");
AssertFalse(eventLog.Contains("TOKEN=123", StringComparison.OrdinalIgnoreCase), "Console event log does not reintroduce secrets");
AssertTrue(AgentConsoleRenderer.RenderEventLog([]).Contains("No recent runtime events", StringComparison.OrdinalIgnoreCase), "Console event log handles empty history");

AssertEqual("1.5 GB", LocalDeviceInfoService.FormatBytes(1_610_612_736), "LocalDeviceInfoService formats byte counts safely");
string systemInfoText = LocalDeviceInfoService.RenderSystemInfo();
AssertTrue(systemInfoText.Contains("Operating system"), "LocalDeviceInfoService renders OS information");
AssertTrue(systemInfoText.Contains("Machine name"), "LocalDeviceInfoService renders machine name");
string diskStatusText = LocalDeviceInfoService.RenderDiskStatus();
AssertTrue(diskStatusText.Contains("Disk status"), "LocalDeviceInfoService renders disk status");
string processText = LocalDeviceInfoService.RenderTopProcesses(5);
AssertTrue(processText.Contains("Running processes"), "LocalDeviceInfoService renders process list");
AssertTrue(processText.Length < 4096, "LocalDeviceInfoService process list is Telegram-safe");

string testFileRoot = Path.Combine(Path.GetTempPath(), "TelegramMessagingTool_FileTests_" + Guid.NewGuid().ToString("N"));
var documentStorage = new DocumentStorageService(testFileRoot, maxFileBytes: 1024 * 1024);
string importDirectory = Path.Combine(testFileRoot, "ImportInbox");
Directory.CreateDirectory(importDirectory);
var testEmbeddingService = new DeterministicEmbeddingService();
var documentEmbeddingService = new DocumentEmbeddingService(testEmbeddingService, "test-embedding-model");
AssertEqual("nomic-embed-text", BotConfiguration.NormalizeEmbeddingModel(""), "BotConfiguration defaults embedding model");
AssertTrue(BotConfiguration.IsEnabled("true", defaultValue: false), "BotConfiguration parses enabled flag");
string? previousAllowPublicAccess = Environment.GetEnvironmentVariable("ALLOW_PUBLIC_ACCESS");
string? previousAllowedChatIdsForConfig = Environment.GetEnvironmentVariable("ALLOWED_CHAT_IDS");
string? previousEnableOnlineSearch = Environment.GetEnvironmentVariable("ENABLE_ONLINE_SEARCH");
string? previousOllamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
string? previousOllamaModelPlan = Environment.GetEnvironmentVariable("OLLAMA_MODEL_PLAN");
string? previousOllamaModelDocQa = Environment.GetEnvironmentVariable("OLLAMA_MODEL_DOC_QA");
string? previousEnableImageVision = Environment.GetEnvironmentVariable("ENABLE_IMAGE_VISION");
string? previousEnableAudioTranscription = Environment.GetEnvironmentVariable("ENABLE_AUDIO_TRANSCRIPTION");
string? previousEnableTelegramTypingIndicator = Environment.GetEnvironmentVariable("ENABLE_TELEGRAM_TYPING_INDICATOR");
string? previousEnableStreamingResponses = Environment.GetEnvironmentVariable("ENABLE_STREAMING_RESPONSES");
try
{
    Environment.SetEnvironmentVariable("ALLOW_PUBLIC_ACCESS", null);
    Environment.SetEnvironmentVariable("ALLOWED_CHAT_IDS", null);
    Environment.SetEnvironmentVariable("ENABLE_ONLINE_SEARCH", null);
    Environment.SetEnvironmentVariable("ENABLE_TELEGRAM_TYPING_INDICATOR", null);
    Environment.SetEnvironmentVariable("ENABLE_STREAMING_RESPONSES", null);
    Environment.SetEnvironmentVariable("OLLAMA_MODEL", "base-model:test");
    Environment.SetEnvironmentVariable("OLLAMA_MODEL_PLAN", null);
    Environment.SetEnvironmentVariable("OLLAMA_MODEL_DOC_QA", null);
    BotSettings defaultPrivacySettings = BotConfiguration.LoadFromEnvironment();
    AssertFalse(defaultPrivacySettings.AllowPublicAccess, "BotConfiguration defaults public access override to false");
    AssertFalse(defaultPrivacySettings.EnableOnlineSearch, "BotConfiguration defaults online search to disabled");
    AssertFalse(defaultPrivacySettings.EnableAudioTranscription, "BotConfiguration defaults audio transcription to disabled");
    AssertFalse(defaultPrivacySettings.EnableTelegramTypingIndicator, "BotConfiguration defaults Telegram typing indicator to disabled");
    AssertFalse(defaultPrivacySettings.EnableStreamingResponses, "BotConfiguration defaults streaming responses to disabled");
    AssertEqual("base-model:test", defaultPrivacySettings.OllamaChatModel, "BotConfiguration defaults chat route to OLLAMA_MODEL");
    AssertEqual("base-model:test", defaultPrivacySettings.OllamaPlanningModel, "BotConfiguration defaults planning route to OLLAMA_MODEL");

    Environment.SetEnvironmentVariable("ALLOW_PUBLIC_ACCESS", "yes");
    AssertTrue(BotConfiguration.LoadFromEnvironment().AllowPublicAccess, "BotConfiguration parses ALLOW_PUBLIC_ACCESS truthy values");

    Environment.SetEnvironmentVariable("ENABLE_ONLINE_SEARCH", "true");
    AssertTrue(BotConfiguration.LoadFromEnvironment().EnableOnlineSearch, "BotConfiguration parses ENABLE_ONLINE_SEARCH truthy values");

    Environment.SetEnvironmentVariable("OLLAMA_MODEL_PLAN", "plan-model:test");
    Environment.SetEnvironmentVariable("OLLAMA_MODEL_DOC_QA", "docqa-model:test");
    Environment.SetEnvironmentVariable("ENABLE_IMAGE_VISION", "true");
    Environment.SetEnvironmentVariable("ENABLE_AUDIO_TRANSCRIPTION", "yes");
    Environment.SetEnvironmentVariable("ENABLE_TELEGRAM_TYPING_INDICATOR", "1");
    Environment.SetEnvironmentVariable("ENABLE_STREAMING_RESPONSES", "true");
    BotSettings routedEnvironmentSettings = BotConfiguration.LoadFromEnvironment();
    AssertEqual("base-model:test", routedEnvironmentSettings.OllamaChatModel, "BotConfiguration keeps chat model on OLLAMA_MODEL when chat override is blank");
    AssertEqual("plan-model:test", routedEnvironmentSettings.OllamaPlanningModel, "BotConfiguration loads OLLAMA_MODEL_PLAN");
    AssertEqual("docqa-model:test", routedEnvironmentSettings.OllamaDocumentQuestionAnsweringModel, "BotConfiguration loads OLLAMA_MODEL_DOC_QA");
    AssertTrue(routedEnvironmentSettings.EnableImageVision, "BotConfiguration parses ENABLE_IMAGE_VISION truthy values");
    AssertTrue(routedEnvironmentSettings.EnableAudioTranscription, "BotConfiguration parses ENABLE_AUDIO_TRANSCRIPTION truthy values");
    AssertTrue(routedEnvironmentSettings.EnableTelegramTypingIndicator, "BotConfiguration parses ENABLE_TELEGRAM_TYPING_INDICATOR truthy values");
    AssertTrue(routedEnvironmentSettings.EnableStreamingResponses, "BotConfiguration parses ENABLE_STREAMING_RESPONSES truthy values");
}
finally
{
    Environment.SetEnvironmentVariable("ALLOW_PUBLIC_ACCESS", previousAllowPublicAccess);
    Environment.SetEnvironmentVariable("ALLOWED_CHAT_IDS", previousAllowedChatIdsForConfig);
    Environment.SetEnvironmentVariable("ENABLE_ONLINE_SEARCH", previousEnableOnlineSearch);
    Environment.SetEnvironmentVariable("OLLAMA_MODEL", previousOllamaModel);
    Environment.SetEnvironmentVariable("OLLAMA_MODEL_PLAN", previousOllamaModelPlan);
    Environment.SetEnvironmentVariable("OLLAMA_MODEL_DOC_QA", previousOllamaModelDocQa);
    Environment.SetEnvironmentVariable("ENABLE_IMAGE_VISION", previousEnableImageVision);
    Environment.SetEnvironmentVariable("ENABLE_AUDIO_TRANSCRIPTION", previousEnableAudioTranscription);
    Environment.SetEnvironmentVariable("ENABLE_TELEGRAM_TYPING_INDICATOR", previousEnableTelegramTypingIndicator);
    Environment.SetEnvironmentVariable("ENABLE_STREAMING_RESPONSES", previousEnableStreamingResponses);
}
AssertEqual("report.md", DocumentStorageService.SanitizeFileName("..\\..//report.md"), "SanitizeFileName removes path segments");

BotSettings typingTestSettings = new(
    BotToken: "test-token",
    OllamaUrl: "http://localhost:11434/api/chat",
    OllamaModel: "qwen3:0.6b",
    OllamaEmbeddingUrl: "http://localhost:11434/api/embed",
    OllamaEmbeddingModel: "nomic-embed-text",
    EnableDocumentEmbeddings: false,
    EnableOnlineSearch: false,
    AdminChatId: 123456789,
    AllowedChatIds: new HashSet<long>(),
    AllowPublicAccess: false,
    DatabaseConnectionString: "Server=(localdb)\\MSSQLLocalDB;Database=TelegramMessagingTool;Trusted_Connection=True;TrustServerCertificate=True",
    ApplyMigrations: true,
    LogMessageContent: false,
    ConversationMaxHistory: 8,
    SearchRoutingMode: "heuristic",
    EnableSafeCommandTools: false,
    SafeCommandProjectRoot: Directory.GetCurrentDirectory(),
    EnablePlugins: false,
    PluginDirectory: Path.Combine(Directory.GetCurrentDirectory(), "plugins"));

int typingSignalCount = 0;
string typingResult = await TelegramTypingService.RunWithTypingLoopAsync(
    async token =>
    {
        Interlocked.Increment(ref typingSignalCount);
        await Task.CompletedTask;
    },
    async token =>
    {
        await Task.Delay(35, token);
        return "typed result";
    },
    TimeSpan.FromMilliseconds(10),
    CancellationToken.None);
AssertEqual("typed result", typingResult, "TelegramTypingService returns the wrapped operation result");
AssertTrue(typingSignalCount >= 2, "TelegramTypingService sends repeated typing signals while work is running");
AssertFalse(TelegramTypingService.ShouldSendTypingIndicator(typingTestSettings with { EnableTelegramTypingIndicator = false }, isCommand: false), "TelegramTypingService is disabled by default/config");
AssertTrue(TelegramTypingService.ShouldSendTypingIndicator(typingTestSettings with { EnableTelegramTypingIndicator = true }, isCommand: false), "TelegramTypingService allows normal messages when enabled");
AssertFalse(TelegramTypingService.ShouldSendTypingIndicator(typingTestSettings with { EnableTelegramTypingIndicator = true }, isCommand: true), "TelegramTypingService does not send typing indicators for commands");

AssertTrue(documentStorage.IsAllowedFileName("notes.txt"), "DocumentStorageService allows txt files");
AssertTrue(documentStorage.IsAllowedFileName("report.md"), "DocumentStorageService allows markdown files");
AssertTrue(documentStorage.IsAllowedFileName("manual.pdf"), "DocumentStorageService allows PDF files");
AssertTrue(documentStorage.IsAllowedFileName("brief.docx"), "DocumentStorageService allows DOCX files");
AssertTrue(documentStorage.IsAllowedFileName("table.xlsx"), "DocumentStorageService allows XLSX files");
AssertTrue(documentStorage.IsAllowedFileName("photo.png"), "DocumentStorageService allows PNG image files");
AssertTrue(documentStorage.IsAllowedFileName("photo.jpg"), "DocumentStorageService allows JPG image files");
AssertTrue(DocumentStorageService.IsImageFileName("photo.webp"), "DocumentStorageService recognizes WEBP image files");
AssertFalse(DocumentStorageService.IsImageFileName("manual.pdf"), "DocumentStorageService does not classify PDFs as images");
AssertTrue(documentStorage.IsAllowedFileName("voice-note.mp3"), "DocumentStorageService allows MP3 audio files");
AssertTrue(documentStorage.IsAllowedFileName("voice-note.ogg"), "DocumentStorageService allows OGG audio files");
AssertTrue(DocumentStorageService.IsAudioFileName("voice-note.opus"), "DocumentStorageService recognizes OPUS audio files");
AssertFalse(DocumentStorageService.IsAudioFileName("photo.png"), "DocumentStorageService does not classify images as audio");
AssertFalse(documentStorage.IsAllowedFileName("malware.exe"), "DocumentStorageService rejects executable files");

var storageTestUser = new ConnectedUser { Id = 42, ChatId = 123456789, Name = "tester" };
UploadedFile createdFile = await documentStorage.CreateTextFileAsync(storageTestUser, "summary.md", "# Summary\nHello document", CancellationToken.None);
AssertTrue(File.Exists(createdFile.AbsolutePath), "CreateTextFileAsync writes sandboxed file");
AssertTrue(createdFile.RelativePath.Contains("123456789"), "CreateTextFileAsync stores under chat sandbox");
AssertFalse(createdFile.RelativePath.Contains(".."), "CreateTextFileAsync does not store traversal path");
string extractedText = await documentStorage.ExtractTextAsync(createdFile, CancellationToken.None);
AssertTrue(extractedText.Contains("Hello document"), "ExtractTextAsync reads created text document");

UploadedFile createdDocx = await documentStorage.CreateFileAsync(storageTestUser, "brief.docx", "DOCX capability works", CancellationToken.None);
string extractedDocx = await documentStorage.ExtractTextAsync(createdDocx, CancellationToken.None);
AssertTrue(extractedDocx.Contains("DOCX capability works"), "ExtractTextAsync reads created DOCX document");

UploadedFile createdXlsx = await documentStorage.CreateFileAsync(storageTestUser, "table.xlsx", "Name,Value\nExcel capability,42", CancellationToken.None);
string extractedXlsx = await documentStorage.ExtractTextAsync(createdXlsx, CancellationToken.None);
AssertTrue(extractedXlsx.Contains("Excel capability"), "ExtractTextAsync reads created XLSX workbook");

UploadedFile createdPdf = await documentStorage.CreateFileAsync(storageTestUser, "manual.pdf", "PDF capability works", CancellationToken.None);
string extractedPdf = await documentStorage.ExtractTextAsync(createdPdf, CancellationToken.None);
AssertTrue(extractedPdf.Contains("PDF capability works"), "ExtractTextAsync reads created PDF document");

IReadOnlyList<string> documentChunks = DocumentChunker.Split(string.Join(" ", Enumerable.Repeat("alpha beta gamma delta", 500)), chunkSize: 300, overlap: 50);
AssertTrue(documentChunks.Count > 1, "DocumentChunker splits long text into multiple chunks");
AssertTrue(documentChunks.All(x => x.Length <= 300), "DocumentChunker respects maximum chunk size");
AssertEqual(0, DocumentChunker.Split("   ").Count, "DocumentChunker ignores blank text");

var retrievalService = new DocumentRetrievalService();
IReadOnlyList<DocumentChunk> rankedChunks = DocumentRetrievalService.RankChunks([
    new DocumentChunk { Id = 1, OriginalFileName = "a.txt", ChunkNumber = 1, Text = "payment deadline is Sunday" },
    new DocumentChunk { Id = 2, OriginalFileName = "b.txt", ChunkNumber = 1, Text = "shipping details only" }
], "what is the payment deadline?", limit: 1);
AssertEqual(1, rankedChunks.Count, "DocumentRetrievalService returns requested limit");
AssertTrue(rankedChunks[0].Text.Contains("payment deadline"), "DocumentRetrievalService ranks relevant chunks first");

IReadOnlyList<DocumentChunk> phraseRankedChunks = DocumentRetrievalService.RankChunks([
    new DocumentChunk { Id = 1, OriginalFileName = "a.txt", ChunkNumber = 1, Text = "payment deadline appears in one exact phrase" },
    new DocumentChunk { Id = 2, OriginalFileName = "b.txt", ChunkNumber = 1, Text = "payment terms are listed elsewhere and the deadline appears later" }
], "payment deadline", limit: 2);
AssertEqual(1, phraseRankedChunks[0].Id, "DocumentRetrievalService boosts exact phrase matches before loose term matches");

string serializedEmbedding = EmbeddingMath.Serialize([1.0, 0.0]);
AssertTrue(serializedEmbedding.Contains("1"), "EmbeddingMath serializes vectors");
IReadOnlyList<float> parsedEmbedding = EmbeddingMath.Parse(serializedEmbedding);
AssertEqual(2, parsedEmbedding.Count, "EmbeddingMath parses serialized vectors");
AssertTrue(EmbeddingMath.CosineSimilarity([1.0f, 0.0f], [1.0f, 0.0f]) > 0.99, "EmbeddingMath scores identical vectors highly");
AssertTrue(EmbeddingMath.CosineSimilarity([1.0f, 0.0f], [0.0f, 1.0f]) < 0.01, "EmbeddingMath scores unrelated vectors low");

IReadOnlyList<DocumentChunk> semanticRankedChunks = DocumentRetrievalService.RankChunksByHybridScore([
    new DocumentChunk { Id = 1, OriginalFileName = "a.txt", ChunkNumber = 1, Text = "contract terms", EmbeddingJson = EmbeddingMath.Serialize([1.0, 0.0]) },
    new DocumentChunk { Id = 2, OriginalFileName = "b.txt", ChunkNumber = 1, Text = "vacation plan", EmbeddingJson = EmbeddingMath.Serialize([0.0, 1.0]) }
], "holiday itinerary", [0.0f, 1.0f], limit: 1);
AssertEqual(2, semanticRankedChunks[0].Id, "DocumentRetrievalService can rank by stored embeddings when available");

AssertTrue(OllamaEmbeddingClient.BuildEmbedUrl("http://localhost:11434/api/chat").EndsWith("/api/embed"), "OllamaEmbeddingClient derives /api/embed from /api/chat");
AssertTrue(OllamaEmbeddingClient.TryParseEmbeddingResponse("{\"embeddings\":[[0.1,0.2,0.3]]}", out IReadOnlyList<float> parsedOllamaEmbedding), "OllamaEmbeddingClient parses /api/embed response");
AssertEqual(3, parsedOllamaEmbedding.Count, "OllamaEmbeddingClient returns parsed vector values");

string qaPrompt = DocumentQuestionAnsweringService.BuildPrompt(
    "what is the payment deadline?",
    [new DocumentChunk { UploadedFileId = 9, OriginalFileName = "contract.pdf", ChunkNumber = 2, Text = "The payment deadline is Sunday." }]);
AssertTrue(qaPrompt.Contains("Use ONLY the document excerpts"), "DocumentQuestionAnsweringService restricts answers to excerpts");
AssertTrue(qaPrompt.Contains("File #9 contract.pdf, chunk 2"), "DocumentQuestionAnsweringService includes chunk citation labels");

string summaryPrompt = DocumentSummaryService.BuildPrompt([
    new DocumentChunk { UploadedFileId = 9, OriginalFileName = "contract.pdf", ChunkNumber = 1, Text = "Contract payment details." }
]);
AssertTrue(summaryPrompt.Contains("Summarize the user's indexed document excerpts"), "DocumentSummaryService creates a summary prompt");
AssertTrue(summaryPrompt.Contains("File #9 contract.pdf, chunk 1"), "DocumentSummaryService includes chunk citation labels");

static Message TextMessage(string text) => new()
{
    Text = text,
    Chat = new Chat { Id = 123456789, Username = "tester", FirstName = "Test", LastName = "User" }
};

string testDbName = $"TelegramMessagingTool_CommandTests_{Guid.NewGuid():N}";
string previousConnection = Environment.GetEnvironmentVariable("TELEGRAM_DB_CONNECTION") ?? string.Empty;
Environment.SetEnvironmentVariable(
    "TELEGRAM_DB_CONNECTION",
    $@"Server=(localdb)\MSSQLLocalDB;Database={testDbName};Trusted_Connection=True;TrustServerCertificate=True");

await using (var dbContext = new TelegramDbContext())
{
    await dbContext.Database.MigrateAsync();

    var testUser = new ConnectedUser
    {
        ChatId = 123456789,
        Name = "tester",
        FirstName = "Test",
        LastName = "User",
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow
    };

    var nonAdminUser = new ConnectedUser
    {
        ChatId = 987654321,
        Name = "nonadmin",
        FirstName = "Non",
        LastName = "Admin",
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow
    };

    dbContext.Users.AddRange(testUser, nonAdminUser);
    await dbContext.SaveChangesAsync();

    var adminTestSettings = new BotSettings(
        BotToken: "test-token",
        OllamaUrl: "http://localhost:11434/api/chat",
        OllamaModel: "qwen3:0.6b",
        OllamaEmbeddingUrl: "http://localhost:11434/api/embed",
        OllamaEmbeddingModel: "nomic-embed-text",
        EnableDocumentEmbeddings: false,
        EnableOnlineSearch: false,
        AdminChatId: testUser.ChatId,
        AllowedChatIds: new HashSet<long>(),
        AllowPublicAccess: false,
        DatabaseConnectionString: Environment.GetEnvironmentVariable("TELEGRAM_DB_CONNECTION")!,
        ApplyMigrations: true,
        LogMessageContent: false,
        ConversationMaxHistory: 8,
        SearchRoutingMode: "heuristic",
        EnableSafeCommandTools: false,
        SafeCommandProjectRoot: Directory.GetCurrentDirectory(),
        EnablePlugins: false,
        PluginDirectory: Path.Combine(Directory.GetCurrentDirectory(), "plugins"))
    {
        OllamaPlanningModel = "qwen3:4b",
        OllamaDocumentQuestionAnsweringModel = "qwen3:8b",
        OllamaImageModel = "llama3.2-vision:11b"
    };

    var pendingActionService = new PendingActionService();
    var runtimeEventBuffer = new RuntimeEventBuffer(capacity: 3);
    runtimeEventBuffer.Record(ConsoleEventLevel.Info, "COMMAND", "ignored info event");
    runtimeEventBuffer.Record(ConsoleEventLevel.Warning, "REMINDER", "provider TOKEN=123456:abcdefghijklmnopqrstuv should redact");
    runtimeEventBuffer.Record(ConsoleEventLevel.Error, "TELEGRAM", "socket reset ghp_secret_value_must_not_render");
    runtimeEventBuffer.Record(ConsoleEventLevel.Warning, "SEARCH", "provider unavailable");

    var dashboardTask = new AgentTask
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        Goal = "Dashboard count test",
        Status = AgentTaskStatuses.Active,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    var dashboardAction = new PendingAction
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        ToolName = "dashboard_test",
        Description = "Dashboard count test",
        PayloadJson = "{}",
        RiskLevel = "medium",
        Status = PendingActionStatuses.Pending,
        ExpiresAt = DateTime.UtcNow.AddMinutes(10)
    };
    var dashboardFile = new UploadedFile
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        OriginalFileName = "dashboard-image.png",
        StoredFileName = "dashboard-image.png",
        RelativePath = "dashboard-image.png",
        AbsolutePath = Path.Combine(testFileRoot, "dashboard-image.png"),
        ContentType = "image/png",
        Source = "test",
        SizeBytes = 10,
        CreatedAt = DateTime.UtcNow
    };
    dbContext.AgentTasks.Add(dashboardTask);
    dbContext.PendingActions.Add(dashboardAction);
    dbContext.UploadedFiles.Add(dashboardFile);
    await dbContext.SaveChangesAsync(CancellationToken.None);
    var dashboardChunk = new DocumentChunk
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        UploadedFileId = dashboardFile.Id,
        OriginalFileName = dashboardFile.OriginalFileName,
        ChunkNumber = 1,
        Text = "dashboard indexed text",
        CharacterCount = "dashboard indexed text".Length,
        CreatedAt = DateTime.UtcNow
    };
    dbContext.DocumentChunks.Add(dashboardChunk);
    await dbContext.SaveChangesAsync(CancellationToken.None);
    string runtimeDashboard = await new RuntimeDashboardService(adminTestSettings, runtimeEventBuffer, DateTimeOffset.UtcNow.AddMinutes(-7))
        .RenderAsync(dbContext, CancellationToken.None);
    AssertTrue(runtimeDashboard.Contains("Runtime dashboard", StringComparison.OrdinalIgnoreCase), "RuntimeDashboardService renders a dashboard");
    AssertTrue(runtimeDashboard.Contains("Active tasks") && runtimeDashboard.Contains("1"), "RuntimeDashboardService counts active tasks");
    AssertTrue(runtimeDashboard.Contains("Pending approvals") && runtimeDashboard.Contains("1"), "RuntimeDashboardService counts pending approvals");
    AssertTrue(runtimeDashboard.Contains("Indexed docs") && runtimeDashboard.Contains("1"), "RuntimeDashboardService counts indexed documents");
    AssertTrue(runtimeDashboard.Contains("Saved images") && runtimeDashboard.Contains("1"), "RuntimeDashboardService counts saved images");
    AssertTrue(runtimeDashboard.Contains("Recent warnings") && runtimeDashboard.Contains("3"), "RuntimeDashboardService counts recent warning/error events within buffer capacity");
    AssertFalse(runtimeDashboard.Contains(adminTestSettings.DatabaseConnectionString, StringComparison.OrdinalIgnoreCase), "RuntimeDashboardService does not expose full DB connection strings");
    dbContext.DocumentChunks.Remove(dashboardChunk);
    dbContext.UploadedFiles.Remove(dashboardFile);
    dbContext.PendingActions.Remove(dashboardAction);
    dbContext.AgentTasks.Remove(dashboardTask);
    await dbContext.SaveChangesAsync(CancellationToken.None);

    var fakeProcessTerminator = new FakeProcessTerminator();
    var fakeLatestReleaseRestarter = new FakeLatestReleaseRestarter();
    var fakeGitHubIssueCreator = new FakeGitHubIssueCreator();
    var fakeGitHubIssueCommenter = new FakeGitHubIssueCommenter();
    var pendingActionExecutor = new PendingActionExecutor(fakeProcessTerminator, documentStorage, fakeLatestReleaseRestarter, fakeGitHubIssueCreator, fakeGitHubIssueCommenter);
    var riskySettings = adminTestSettings with
    {
        AllowPublicAccess = true,
        LogMessageContent = true,
        EnableRepoWriteTools = true,
        EnableSafeCommandTools = true,
        EnablePlugins = true,
        SearchRoutingMode = "llm",
        EnableAudioTranscription = true,
        AudioTranscriptionCommand = string.Empty,
        EnableTextToSpeech = true,
        TextToSpeechCommand = string.Empty,
        GitHub = adminTestSettings.GitHub with
        {
            EnableGitHubWriteTools = true,
            Token = "ghp_secret_value_must_not_render"
        }
    };
    var riskConfigCommand = new RiskConfigCommand(riskySettings);
    CommandResult riskConfigResult = await riskConfigCommand.TryHandleAsync(TextMessage("/riskconfig"), testUser, dbContext, CancellationToken.None);
    AssertTrue(riskConfigResult.Handled, "/riskconfig is handled");
    AssertTrue(riskConfigResult.ReplyText?.Contains("Risk configuration summary", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig includes title");
    AssertTrue(riskConfigResult.ReplyText?.Contains("Public access: ENABLED", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig warns about public access");
    AssertTrue(riskConfigResult.ReplyText?.Contains("Message content logging: ENABLED", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig warns about content logging");
    AssertTrue(riskConfigResult.ReplyText?.Contains("Repo write tools: ENABLED", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig shows repo write tools");
    AssertTrue(riskConfigResult.ReplyText?.Contains("GitHub write tools: ENABLED", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig shows GitHub write tools");
    AssertTrue(riskConfigResult.ReplyText?.Contains("Plugin loading: ENABLED", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig shows plugin loading");
    AssertTrue(riskConfigResult.ReplyText?.Contains("Safe command tools: ENABLED", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig shows safe command tools");
    AssertTrue(riskConfigResult.ReplyText?.Contains("Search routing: llm", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig shows search routing mode");
    AssertTrue(riskConfigResult.ReplyText?.Contains("Audio transcription: enabled, provider command missing", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig warns when transcription provider command is missing");
    AssertTrue(riskConfigResult.ReplyText?.Contains("TTS: enabled, provider command missing", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig warns when TTS provider command is missing");
    AssertFalse(riskConfigResult.ReplyText?.Contains("ghp_secret_value_must_not_render", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig never renders GitHub token values");
    AssertFalse(riskConfigResult.ReplyText?.Contains(adminTestSettings.DatabaseConnectionString, StringComparison.OrdinalIgnoreCase) == true, "/riskconfig never renders DB connection string values");
    IReadOnlyList<string> startupRiskWarnings = RuntimeRiskSummary.RenderStartupWarnings(
        riskySettings,
        BotAccessPolicy.DescribeAccessMode(riskySettings.AllowedChatIds, riskySettings.AdminChatId, riskySettings.AllowPublicAccess));
    AssertTrue(startupRiskWarnings.Any(x => x.Contains("ALLOW_PUBLIC_ACCESS", StringComparison.OrdinalIgnoreCase)), "Startup risk warnings include public access");
    AssertTrue(startupRiskWarnings.Any(x => x.Contains("LOG_MESSAGE_CONTENT", StringComparison.OrdinalIgnoreCase)), "Startup risk warnings include content logging");
    AssertTrue(startupRiskWarnings.Any(x => x.Contains("repo write", StringComparison.OrdinalIgnoreCase)), "Startup risk warnings include repo write tools");
    AssertTrue(startupRiskWarnings.Any(x => x.Contains("plugin", StringComparison.OrdinalIgnoreCase)), "Startup risk warnings include plugin loading");
    AssertTrue(startupRiskWarnings.Any(x => x.Contains("GitHub write", StringComparison.OrdinalIgnoreCase)), "Startup risk warnings include GitHub write tools");
    AssertTrue(startupRiskWarnings.Any(x => x.Contains("audio transcription", StringComparison.OrdinalIgnoreCase) && x.Contains("missing", StringComparison.OrdinalIgnoreCase)), "Startup risk warnings include missing audio provider command");
    AssertTrue(startupRiskWarnings.Any(x => x.Contains("TTS", StringComparison.OrdinalIgnoreCase) && x.Contains("missing", StringComparison.OrdinalIgnoreCase)), "Startup risk warnings include missing TTS provider command");
    string startupPanel = AgentConsoleRenderer.RenderStartupPanel(new AgentConsoleSnapshot(
        BotUsername: "red_eye_ghost_bot",
        OllamaUrl: riskySettings.OllamaUrl,
        OllamaModel: riskySettings.OllamaModel,
        DatabaseConnection: riskySettings.DatabaseConnectionString,
        AccessMode: BotAccessPolicy.DescribeAccessMode(riskySettings.AllowedChatIds, riskySettings.AdminChatId, riskySettings.AllowPublicAccess),
        MessageContentLoggingEnabled: riskySettings.LogMessageContent,
        OnlineSearchEnabled: riskySettings.EnableOnlineSearch,
        ApplyMigrations: riskySettings.ApplyMigrations,
        Commands: ["/riskconfig"],
        Tools: ["dotnet_create_project"],
        RiskWarnings: startupRiskWarnings));
    AssertTrue(startupPanel.Contains("Runtime risk warnings", StringComparison.OrdinalIgnoreCase), "Startup panel uses consolidated runtime risk warning title");
    AssertTrue(startupPanel.Contains("repo write", StringComparison.OrdinalIgnoreCase), "Startup panel shows repo write risk warning");
    AssertFalse(startupPanel.Contains("ghp_secret_value_must_not_render", StringComparison.OrdinalIgnoreCase), "Startup panel does not render GitHub token values");
    AssertFalse(startupPanel.Contains(adminTestSettings.DatabaseConnectionString, StringComparison.OrdinalIgnoreCase), "Startup panel does not render full DB connection string");
    CommandResult nonAdminRiskConfigResult = await riskConfigCommand.TryHandleAsync(TextMessage("/riskconfig"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminRiskConfigResult.Handled, "/riskconfig non-admin attempt is handled");
    AssertTrue(nonAdminRiskConfigResult.ReplyText?.Contains("admin-only", StringComparison.OrdinalIgnoreCase) == true, "/riskconfig is admin-only");
    AssertFalse((await riskConfigCommand.TryHandleAsync(TextMessage("/riskconfigx"), testUser, dbContext, CancellationToken.None)).Handled, "/riskconfigx is not treated as /riskconfig");

    List<string> callbackAuditEvents = [];
    var callbackAuditObservability = new RuntimeObservabilityService(callbackAuditEvents.Add);
    var pendingActionCallbackService = new PendingActionCallbackService(pendingActionService, pendingActionExecutor, adminTestSettings, callbackAuditObservability);
    PendingActionCallbackResult unauthorizedPendingCallback = await pendingActionCallbackService.HandleAsync(
        "act:approve:1",
        testUser,
        actorTelegramUserId: nonAdminUser.ChatId,
        dbContext,
        CancellationToken.None);
    AssertTrue(unauthorizedPendingCallback.Handled, "Pending action callback handles unauthorized actor attempts");
    AssertEqual("Not authorized", unauthorizedPendingCallback.AnswerText, "Pending action callback rejects a non-admin callback actor even when the message chat belongs to admin");
    AssertTrue(unauthorizedPendingCallback.MessageText?.Contains("not authorized", StringComparison.OrdinalIgnoreCase) == true, "Pending action callback explains actor authorization failure");
    AssertTrue(callbackAuditEvents.Any(x => x.Contains("CALLBACK_DECISION", StringComparison.OrdinalIgnoreCase) && x.Contains("domain=pending_action", StringComparison.OrdinalIgnoreCase) && x.Contains("status=rejected", StringComparison.OrdinalIgnoreCase) && x.Contains("actor=", StringComparison.OrdinalIgnoreCase)), "Pending action callback logs rejected actor metadata");

    var actorTaskCallbackService = new TaskCallbackService(new AgentTaskService(), adminTestSettings, callbackAuditObservability);
    TaskCallbackResult unauthorizedTaskCallback = await actorTaskCallbackService.HandleAsync(
        "task:done:1",
        testUser,
        actorTelegramUserId: nonAdminUser.ChatId,
        dbContext,
        CancellationToken.None);
    AssertTrue(unauthorizedTaskCallback.Handled, "Task callback handles unauthorized actor attempts");
    AssertEqual("Not authorized", unauthorizedTaskCallback.AnswerText, "Task callback rejects a different callback actor even when the message chat belongs to task owner");
    AssertTrue(unauthorizedTaskCallback.MessageText?.Contains("not authorized", StringComparison.OrdinalIgnoreCase) == true, "Task callback explains actor authorization failure");
    AssertTrue(callbackAuditEvents.Any(x => x.Contains("CALLBACK_DECISION", StringComparison.OrdinalIgnoreCase) && x.Contains("domain=task", StringComparison.OrdinalIgnoreCase) && x.Contains("status=rejected", StringComparison.OrdinalIgnoreCase)), "Task callback logs rejected actor metadata");
    AssertFalse(string.Join('\n', callbackAuditEvents).Contains("old_text", StringComparison.OrdinalIgnoreCase), "Callback audit events do not log raw pending-action payload JSON");

    var gitHubWriteSettings = adminTestSettings with
    {
        GitHub = gitHubSettings with { EnableGitHubWriteTools = true }
    };
    ToolRegistry gitHubWriteApprovalRegistry = ToolRegistryFactory.Create(gitHubWriteSettings, new HttpClient(), pendingActionService);
    AssertTrue(gitHubWriteApprovalRegistry.TryGet("github_create_issue", out IAgentTool? gitHubCreateIssueTool), "ToolRegistryFactory includes github_create_issue when GitHub write tools and pending service are enabled");
    AssertTrue(gitHubCreateIssueTool!.RequiresApproval, "github_create_issue requires approval");
    AssertTrue(gitHubWriteApprovalRegistry.TryGet("github_comment_issue", out IAgentTool? gitHubCommentIssueTool), "ToolRegistryFactory includes github_comment_issue when GitHub write tools and pending service are enabled");
    AssertTrue(gitHubCommentIssueTool!.RequiresApproval, "github_comment_issue requires approval");
    var createIssueRequestChatClient = new ScriptedChatClient([
        "{\"type\":\"tool_call\",\"tool\":\"github_create_issue\",\"input\":\"{\\\"title\\\":\\\"Test issue from approval flow\\\",\\\"body\\\":\\\"Created only after approval.\\\",\\\"labels\\\":[\\\"agent-tools\\\"]}\"}"
    ]);
    string createIssueRequestReply = await new AgentRunner(
        createIssueRequestChatClient,
        gitHubWriteApprovalRegistry,
        searchRoutingClassifier: new OffSearchRoutingClassifier()).RunAsync(
            [new OllamaMessageDto("user", "request a GitHub issue creation approval")],
            CancellationToken.None,
            dbContext,
            testUser);
    AssertTrue(createIssueRequestReply.Contains("Pending action #"), "AgentRunner creates a pending action for github_create_issue");
    PendingAction createIssueAction = await dbContext.PendingActions.OrderByDescending(x => x.Id).FirstAsync(x => x.ToolName == "github_create_issue", CancellationToken.None);
    AssertEqual(PendingActionStatuses.Pending, createIssueAction.Status, "github_create_issue request is stored as pending");
    AssertTrue(createIssueAction.PayloadJson.Contains("Test issue from approval flow"), "github_create_issue pending action stores the title");
    AssertEqual(0, fakeGitHubIssueCreator.CreateCallCount, "github_create_issue request does not call GitHub before approval");
    PendingActionDecision createIssueApproval = await pendingActionService.ApproveAsync(dbContext, testUser, createIssueAction.Id, CancellationToken.None);
    PendingActionExecutionResult createIssueExecution = await pendingActionExecutor.ExecuteApprovedAsync(dbContext, createIssueApproval.Action!, CancellationToken.None);
    AssertTrue(createIssueExecution.Executed, "github_create_issue approval executes automatically");
    AssertTrue(createIssueExecution.Success, "github_create_issue execution succeeds through injected creator");
    AssertEqual(1, fakeGitHubIssueCreator.CreateCallCount, "github_create_issue calls GitHub creator once after approval");
    AssertEqual("Test issue from approval flow", fakeGitHubIssueCreator.LastPayload?.Title, "github_create_issue passes title to creator");
    AssertTrue((await dbContext.PendingActions.FindAsync([createIssueAction.Id], CancellationToken.None))!.DecisionNote.Contains("https://github.com/mujahedgt/TelegramMessagingTool/issues/777"), "github_create_issue records created issue URL in DecisionNote");
    var directCreateIssueTool = new GitHubCreateIssueRequestTool(pendingActionService, gitHubWriteSettings);
    ToolResult nonAdminCreateIssueResult = await directCreateIssueTool.CreatePendingActionAsync(
        "{\"title\":\"Unauthorized issue\",\"body\":\"Nope\"}",
        dbContext,
        nonAdminUser,
        CancellationToken.None);
    AssertFalse(nonAdminCreateIssueResult.Success, "github_create_issue requires admin before creating pending actions");
    ToolResult invalidCreateIssueResult = await directCreateIssueTool.CreatePendingActionAsync(
        "{\"title\":\"\"}",
        dbContext,
        testUser,
        CancellationToken.None);
    AssertFalse(invalidCreateIssueResult.Success, "github_create_issue rejects empty issue titles");
    var commentIssueRequestChatClient = new ScriptedChatClient([
        "{\"type\":\"tool_call\",\"tool\":\"github_comment_issue\",\"input\":\"{\\\"number\\\":777,\\\"body\\\":\\\"Approved comment from agent flow.\\\"}\"}"
    ]);
    string commentIssueRequestReply = await new AgentRunner(
        commentIssueRequestChatClient,
        gitHubWriteApprovalRegistry,
        searchRoutingClassifier: new OffSearchRoutingClassifier()).RunAsync(
            [new OllamaMessageDto("user", "request a GitHub issue comment approval")],
            CancellationToken.None,
            dbContext,
            testUser);
    AssertTrue(commentIssueRequestReply.Contains("Pending action #"), "AgentRunner creates a pending action for github_comment_issue");
    PendingAction commentIssueAction = await dbContext.PendingActions.OrderByDescending(x => x.Id).FirstAsync(x => x.ToolName == "github_comment_issue", CancellationToken.None);
    AssertEqual(PendingActionStatuses.Pending, commentIssueAction.Status, "github_comment_issue request is stored as pending");
    AssertTrue(commentIssueAction.PayloadJson.Contains("Approved comment from agent flow"), "github_comment_issue pending action stores the comment body");
    AssertEqual(0, fakeGitHubIssueCommenter.CommentCallCount, "github_comment_issue request does not call GitHub before approval");
    PendingActionDecision commentIssueApproval = await pendingActionService.ApproveAsync(dbContext, testUser, commentIssueAction.Id, CancellationToken.None);
    PendingActionExecutionResult commentIssueExecution = await pendingActionExecutor.ExecuteApprovedAsync(dbContext, commentIssueApproval.Action!, CancellationToken.None);
    AssertTrue(commentIssueExecution.Executed, "github_comment_issue approval executes automatically");
    AssertTrue(commentIssueExecution.Success, "github_comment_issue execution succeeds through injected commenter");
    AssertEqual(1, fakeGitHubIssueCommenter.CommentCallCount, "github_comment_issue calls GitHub commenter once after approval");
    AssertEqual(777, fakeGitHubIssueCommenter.LastPayload?.Number, "github_comment_issue passes issue number to commenter");
    AssertTrue((await dbContext.PendingActions.FindAsync([commentIssueAction.Id], CancellationToken.None))!.DecisionNote.Contains("#777"), "github_comment_issue records comment URL in DecisionNote");
    var directCommentIssueTool = new GitHubCommentIssueRequestTool(pendingActionService, gitHubWriteSettings);
    ToolResult nonAdminCommentIssueResult = await directCommentIssueTool.CreatePendingActionAsync(
        "{\"number\":777,\"body\":\"Unauthorized comment\"}",
        dbContext,
        nonAdminUser,
        CancellationToken.None);
    AssertFalse(nonAdminCommentIssueResult.Success, "github_comment_issue requires admin before creating pending actions");
    ToolResult invalidCommentIssueResult = await directCommentIssueTool.CreatePendingActionAsync(
        "{\"number\":0,\"body\":\"Nope\"}",
        dbContext,
        testUser,
        CancellationToken.None);
    AssertFalse(invalidCommentIssueResult.Success, "github_comment_issue rejects invalid issue numbers");
    ToolResult emptyCommentIssueResult = await directCommentIssueTool.CreatePendingActionAsync(
        "{\"number\":777,\"body\":\"\"}",
        dbContext,
        testUser,
        CancellationToken.None);
    AssertFalse(emptyCommentIssueResult.Success, "github_comment_issue rejects empty comment bodies");
    var agentTaskService = new AgentTaskService();
    var documentIndexingService = new DocumentIndexingService(documentStorage);
    var documentRetrievalService = new DocumentRetrievalService(testEmbeddingService);
    var documentQaChatClient = new ScriptedChatClient([
        "The payment deadline is Sunday. Source: File #1 notes.md, chunk 1.",
        "The saved note says this is a saved note. Source: File #1 notes.md, chunk 1.",
        "Summary: this document contains a saved note. Source: File #1 notes.md, chunk 1.",
        "Summary: indexed documents include a saved note. Source: File #1 notes.md, chunk 1."
    ]);
    var documentQuestionAnsweringService = new DocumentQuestionAnsweringService(documentQaChatClient);
    var documentSummaryChatClient = new ScriptedChatClient([
        "Summary: this document contains a saved note. Source: File #1 notes.md, chunk 1.",
        "Summary: indexed documents include a saved note. Source: File #1 notes.md, chunk 1."
    ]);
    var documentSummaryService = new DocumentSummaryService(documentSummaryChatClient);
    var commandRouter = new CommandRouter([
        new HelpCommand(),
        new SystemInfoCommand(),
        new DiskStatusCommand(),
        new ProcessesCommand(),
        new StatusCommand(adminTestSettings),
        new HealthCommand(adminTestSettings, documentStorage, importDirectory),
        new ErrorsCommand(adminTestSettings, runtimeEventBuffer),
        new RiskConfigCommand(adminTestSettings),
        new ResetCommand(),
        new RememberCommand(),
        new MemoryCommand(),
        new ForgetCommand(),
        new FilesCommand(documentStorage),
        new ImagesCommand(),
        new DescribeImageCommand(adminTestSettings, documentStorage),
        new VoiceFilesCommand(),
        new TranscribeCommand(adminTestSettings, documentStorage),
        new TranscriptInsightsCommand(documentStorage, new TranscriptInsightsService(new ScriptedChatClient(["Voice summary: command router transcript insight fixture."]))),
        new TranscriptTasksCommand(documentStorage, new TranscriptInsightsService(new ScriptedChatClient(["Proposed title: command router transcript task fixture\nDraft task list:\n- Follow up\nSuggested /plan command: /plan Follow up\nMissing information: none"]))),
        new SpeakTextCommand(adminTestSettings, documentStorage),
        new SendAudioCommand(documentStorage),
        new ExportChatCommand(documentStorage),
        new ReadFileCommand(documentStorage),
        new CreateFileCommand(documentStorage),
        new ImportFilesCommand(importDirectory, documentStorage, adminTestSettings),
        new ImportFileCommand(importDirectory, documentStorage, adminTestSettings),
        new DeleteFileCommand(pendingActionService, adminTestSettings),
        new IndexFileCommand(documentIndexingService),
        new IndexDocsCommand(documentIndexingService),
        new DocChunksCommand(),
        new AskFileCommand(documentIndexingService, documentRetrievalService, documentQuestionAnsweringService),
        new AskDocsCommand(documentRetrievalService, documentQuestionAnsweringService),
        new SummarizeFileCommand(documentIndexingService, documentRetrievalService, documentSummaryService),
        new SummarizeDocsCommand(documentRetrievalService, documentSummaryService),
        new EmbedFileCommand(documentIndexingService, documentEmbeddingService),
        new EmbedDocsCommand(documentIndexingService, documentEmbeddingService),
        new ToolsCommand(registry),
        new HarnessesCommand(adminTestSettings),
        new PluginsCommand(adminTestSettings),
        new KillProcessCommand(pendingActionService, adminTestSettings),
        new ActionCommand(pendingActionService, adminTestSettings),
        new PendingCommand(pendingActionService, adminTestSettings),
        new ApproveCommand(pendingActionService, pendingActionExecutor, adminTestSettings),
        new DenyCommand(pendingActionService, adminTestSettings),
        new PlanCommand(agentTaskService),
        new TasksCommand(agentTaskService),
        new TaskCommand(agentTaskService),
        new ScheduleCommand(agentTaskService),
        new ScheduleListCommand(agentTaskService),
        new UnscheduleCommand(agentTaskService),
        new DoneCommand(agentTaskService),
        new CancelCommand(agentTaskService)
    ]);

    dbContext.Messages.AddRange(
        new ChatMessage
        {
            ConnectedUserId = testUser.Id,
            ChatId = testUser.ChatId,
            Role = ChatRoles.User,
            Content = "First export message",
            Timestamp = new DateTime(2026, 7, 7, 18, 0, 0, DateTimeKind.Utc)
        },
        new ChatMessage
        {
            ConnectedUserId = testUser.Id,
            ChatId = testUser.ChatId,
            Role = ChatRoles.Assistant,
            Content = "Second export answer",
            Timestamp = new DateTime(2026, 7, 7, 18, 1, 0, DateTimeKind.Utc)
        },
        new ChatMessage
        {
            ConnectedUserId = nonAdminUser.Id,
            ChatId = nonAdminUser.ChatId,
            Role = ChatRoles.User,
            Content = "Other user private message must not export",
            Timestamp = new DateTime(2026, 7, 7, 18, 2, 0, DateTimeKind.Utc)
        });
    await dbContext.SaveChangesAsync(CancellationToken.None);

    CommandResult exportChatResult = await new ExportChatCommand(documentStorage)
        .TryHandleAsync(TextMessage("/exportchat txt last 2"), testUser, dbContext, CancellationToken.None);
    AssertTrue(exportChatResult.Handled, "/exportchat txt last N is handled");
    AssertTrue(exportChatResult.ReplyText?.Contains("Chat export created", StringComparison.OrdinalIgnoreCase) == true, "/exportchat confirms export creation");
    AssertTrue(exportChatResult.DocumentFile is not null, "/exportchat returns a document attachment for Telegram sending");
    AssertTrue(File.Exists(exportChatResult.DocumentFile!.AbsolutePath), "/exportchat writes the export file inside document storage");
    string exportedChatText = await File.ReadAllTextAsync(exportChatResult.DocumentFile.AbsolutePath, CancellationToken.None);
    AssertTrue(exportedChatText.Contains("First export message", StringComparison.OrdinalIgnoreCase), "/exportchat includes the user's selected history");
    AssertTrue(exportedChatText.Contains("Second export answer", StringComparison.OrdinalIgnoreCase), "/exportchat includes assistant messages");
    AssertFalse(exportedChatText.Contains("Other user private message", StringComparison.OrdinalIgnoreCase), "/exportchat exports only the current user's chat history");

    CommandResult exportChatDocxResult = await new ExportChatCommand(documentStorage)
        .TryHandleAsync(TextMessage("/exportchat docx last 1"), testUser, dbContext, CancellationToken.None);
    AssertTrue(exportChatDocxResult.Handled, "/exportchat docx last N is handled");
    AssertTrue(exportChatDocxResult.DocumentFile is not null, "/exportchat docx returns a document attachment");
    AssertTrue(exportChatDocxResult.DocumentFile!.OriginalFileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase), "/exportchat docx creates a DOCX file");
    string exportedDocxText = await documentStorage.ExtractTextAsync(exportChatDocxResult.DocumentFile, CancellationToken.None);
    AssertTrue(exportedDocxText.Contains("Second export answer", StringComparison.OrdinalIgnoreCase), "/exportchat docx includes the selected chat history");
    AssertFalse(exportedDocxText.Contains("First export message", StringComparison.OrdinalIgnoreCase), "/exportchat docx respects last N");

    CommandResult exportChatPdfResult = await new ExportChatCommand(documentStorage)
        .TryHandleAsync(TextMessage("/exportchat pdf last 2"), testUser, dbContext, CancellationToken.None);
    AssertTrue(exportChatPdfResult.Handled, "/exportchat pdf last N is handled");
    AssertTrue(exportChatPdfResult.DocumentFile is not null, "/exportchat pdf returns a document attachment");
    AssertTrue(exportChatPdfResult.DocumentFile!.OriginalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase), "/exportchat pdf creates a PDF file");
    string exportedPdfText = await documentStorage.ExtractTextAsync(exportChatPdfResult.DocumentFile, CancellationToken.None);
    AssertTrue(exportedPdfText.Contains("First export message", StringComparison.OrdinalIgnoreCase), "/exportchat pdf includes selected user history");
    AssertTrue(exportedPdfText.Contains("Second export answer", StringComparison.OrdinalIgnoreCase), "/exportchat pdf includes assistant history");
    AssertFalse(exportedPdfText.Contains("Other user private message", StringComparison.OrdinalIgnoreCase), "/exportchat pdf excludes other users");

    AssertTrue((await new ExportChatCommand(documentStorage).TryHandleAsync(TextMessage("/exportchat xlsx last 2"), testUser, dbContext, CancellationToken.None)).ReplyText?.Contains("TXT, DOCX, or PDF", StringComparison.OrdinalIgnoreCase) == true, "/exportchat clearly limits this phase to TXT, DOCX, or PDF");
    AssertFalse((await new ExportChatCommand(documentStorage).TryHandleAsync(TextMessage("/exportchatx txt last 2"), testUser, dbContext, CancellationToken.None)).Handled, "/exportchatx is not treated as /exportchat");
    dbContext.UploadedFiles.RemoveRange(exportChatResult.DocumentFile!, exportChatDocxResult.DocumentFile!, exportChatPdfResult.DocumentFile!);
    await dbContext.SaveChangesAsync(CancellationToken.None);
    File.Delete(exportChatResult.DocumentFile.AbsolutePath);
    File.Delete(exportChatDocxResult.DocumentFile.AbsolutePath);
    File.Delete(exportChatPdfResult.DocumentFile.AbsolutePath);

    string pluginCommandRoot = Path.Combine(Path.GetTempPath(), $"TelegramMessagingTool_PluginsCommand_{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path.Combine(pluginCommandRoot, "SamplePlugin"));
    await File.WriteAllTextAsync(Path.Combine(pluginCommandRoot, "SamplePlugin", "plugin.json"), """
    {
      "id": "sample-plugin",
      "name": "Sample Plugin",
      "version": "1.0.0",
      "apiVersion": "1.0",
      "entryAssembly": "SamplePlugin.dll",
      "enabled": true,
      "riskLevel": "low",
      "allowedToolNames": ["sample_tool"]
    }
    """);
    await File.WriteAllTextAsync(Path.Combine(pluginCommandRoot, "SamplePlugin", "SamplePlugin.dll"), "placeholder assembly fixture", CancellationToken.None);
    Directory.CreateDirectory(Path.Combine(pluginCommandRoot, "MissingAssemblyPlugin"));
    await File.WriteAllTextAsync(Path.Combine(pluginCommandRoot, "MissingAssemblyPlugin", "plugin.json"), """
    {
      "id": "missing-assembly-plugin",
      "name": "Missing Assembly Plugin",
      "version": "1.0.0",
      "apiVersion": "1.0",
      "entryAssembly": "MissingAssemblyPlugin.dll",
      "enabled": false,
      "riskLevel": "medium",
      "allowedToolNames": ["missing_assembly_tool"]
    }
    """);
    var pluginCommand = new PluginsCommand(adminTestSettings with
    {
        EnablePlugins = true,
        PluginDirectory = pluginCommandRoot
    });
    CommandResult pluginsResult = await pluginCommand.TryHandleAsync(TextMessage("/plugins"), testUser, dbContext, CancellationToken.None);
    AssertTrue(pluginsResult.Handled, "/plugins is handled");
    AssertTrue(pluginsResult.ReplyText?.Contains("sample-plugin") == true, "/plugins lists discovered manifest id");
    AssertTrue(pluginsResult.ReplyText?.Contains("plugin.json") == true, "/plugins shows manifest path details");
    AssertTrue(pluginsResult.ReplyText?.Contains("SamplePlugin.dll (present)") == true, "/plugins reports present entry assembly fixture");
    AssertTrue(pluginsResult.ReplyText?.Contains("MissingAssemblyPlugin.dll (missing)") == true, "/plugins reports missing entry assembly safely");
    AssertTrue(pluginsResult.ReplyText?.Contains("Assembly loading: enabled", StringComparison.OrdinalIgnoreCase) == true, "/plugins explains trusted assembly loading is enabled");
    AssertTrue(pluginsResult.ReplyText?.Contains("sample_tool") == true, "/plugins lists allowed tool names");

    CommandResult pluginsMentionResult = await pluginCommand.TryHandleAsync(TextMessage("/plugins@red_eye_ghost_bot"), testUser, dbContext, CancellationToken.None);
    AssertTrue(pluginsMentionResult.Handled, "/plugins@bot is handled");
    AssertFalse((await pluginCommand.TryHandleAsync(TextMessage("/pluginsx"), testUser, dbContext, CancellationToken.None)).Handled, "/pluginsx is not treated as /plugins");

    var missingPluginsCommand = new PluginsCommand(adminTestSettings with
    {
        PluginDirectory = Path.Combine(pluginCommandRoot, "missing")
    });
    CommandResult missingPluginsResult = await missingPluginsCommand.TryHandleAsync(TextMessage("/plugins"), testUser, dbContext, CancellationToken.None);
    AssertTrue(missingPluginsResult.Handled, "/plugins missing directory is handled");
    AssertTrue(missingPluginsResult.ReplyText?.Contains("does not exist", StringComparison.OrdinalIgnoreCase) == true, "/plugins reports missing plugin directory diagnostic");
    Directory.Delete(pluginCommandRoot, recursive: true);

    DateTime scheduledStepTime = new(2026, 6, 28, 18, 30, 0, DateTimeKind.Utc);
    var scheduledTask = new AgentTask
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        Goal = "Scheduled reminder persistence test",
        Status = AgentTaskStatuses.Active,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Steps =
        [
            new AgentTaskStep
            {
                StepNumber = 1,
                Description = "Prepare reminder storage.",
                ScheduledAtUtc = scheduledStepTime,
                ScheduleNote = "Review scheduled task storage",
                CreatedAt = DateTime.UtcNow
            },
            new AgentTaskStep
            {
                StepNumber = 2,
                Description = "Already reminded step.",
                ScheduledAtUtc = scheduledStepTime.AddHours(1),
                ReminderSentAtUtc = scheduledStepTime.AddHours(1).AddMinutes(5),
                CreatedAt = DateTime.UtcNow
            }
        ]
    };
    dbContext.AgentTasks.Add(scheduledTask);
    await dbContext.SaveChangesAsync(CancellationToken.None);

    AgentTask persistedScheduledTask = await dbContext.AgentTasks
        .Include(x => x.Steps)
        .SingleAsync(x => x.Id == scheduledTask.Id, CancellationToken.None);
    AgentTaskStep persistedScheduledStep = persistedScheduledTask.Steps.Single(x => x.StepNumber == 1);
    AssertEqual(scheduledStepTime, persistedScheduledStep.ScheduledAtUtc, "AgentTaskStep persists ScheduledAtUtc");
    AssertEqual("Review scheduled task storage", persistedScheduledStep.ScheduleNote, "AgentTaskStep persists ScheduleNote");
    AssertTrue(persistedScheduledTask.Steps.Single(x => x.StepNumber == 2).ReminderSentAtUtc.HasValue, "AgentTaskStep persists ReminderSentAtUtc");
    string renderedScheduledTask = AgentTaskService.RenderTask(persistedScheduledTask);
    AssertTrue(renderedScheduledTask.Contains("scheduled 2026-06-28 18:30 UTC", StringComparison.OrdinalIgnoreCase), "RenderTask shows scheduled step time");
    AssertTrue(renderedScheduledTask.Contains("Review scheduled task storage", StringComparison.OrdinalIgnoreCase), "RenderTask shows schedule note");
    AssertTrue(renderedScheduledTask.Contains("reminded 2026-06-28 19:35 UTC", StringComparison.OrdinalIgnoreCase), "RenderTask shows reminder sent time");
    dbContext.AgentTasks.Remove(persistedScheduledTask);
    await dbContext.SaveChangesAsync(CancellationToken.None);

    DateTime reminderNow = new(2026, 6, 28, 20, 0, 0, DateTimeKind.Utc);
    var dueReminderTask = new AgentTask
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        Goal = "Due reminder test",
        Status = AgentTaskStatuses.Active,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Steps =
        [
            new AgentTaskStep
            {
                StepNumber = 1,
                Description = "Send due reminder.",
                ScheduledAtUtc = reminderNow.AddMinutes(-5),
                ScheduleNote = "Important reminder note",
                CreatedAt = DateTime.UtcNow
            },
            new AgentTaskStep
            {
                StepNumber = 2,
                Description = "Future reminder.",
                ScheduledAtUtc = reminderNow.AddMinutes(5),
                CreatedAt = DateTime.UtcNow
            },
            new AgentTaskStep
            {
                StepNumber = 3,
                Description = "Already sent reminder.",
                ScheduledAtUtc = reminderNow.AddMinutes(-10),
                ReminderSentAtUtc = reminderNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow
            }
        ]
    };
    var completedDueReminderTask = new AgentTask
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        Goal = "Completed reminder test",
        Status = AgentTaskStatuses.Completed,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        CompletedAt = DateTime.UtcNow,
        Steps =
        [
            new AgentTaskStep
            {
                StepNumber = 1,
                Description = "Do not send completed task reminder.",
                ScheduledAtUtc = reminderNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow
            }
        ]
    };
    dbContext.AgentTasks.AddRange(dueReminderTask, completedDueReminderTask);
    await dbContext.SaveChangesAsync(CancellationToken.None);

    var fakeReminderSender = new FakeTaskReminderSender();
    var taskReminderService = new TaskReminderService(fakeReminderSender, () => reminderNow);
    ReminderScanResult reminderScanResult = await taskReminderService.SendDueRemindersAsync(dbContext, CancellationToken.None);
    AssertEqual(1, reminderScanResult.SentCount, "TaskReminderService sends one due unsent active reminder");
    AssertEqual(1, fakeReminderSender.SentMessages.Count, "TaskReminderService calls sender once");
    AssertEqual(testUser.ChatId, fakeReminderSender.SentMessages[0].ChatId, "TaskReminderService sends reminder to task chat");
    AssertTrue(fakeReminderSender.SentMessages[0].Text.Contains("Due reminder test"), "TaskReminderService reminder includes task goal");
    AssertTrue(fakeReminderSender.SentMessages[0].Text.Contains("Important reminder note"), "TaskReminderService reminder includes schedule note");
    AgentTaskStep dueReminderStep = await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == dueReminderTask.Id && x.StepNumber == 1, CancellationToken.None);
    AssertEqual(reminderNow, dueReminderStep.ReminderSentAtUtc, "TaskReminderService marks reminder sent after successful send");
    ReminderScanResult secondReminderScanResult = await taskReminderService.SendDueRemindersAsync(dbContext, CancellationToken.None);
    AssertEqual(0, secondReminderScanResult.SentCount, "TaskReminderService does not resend already marked reminders");
    AssertEqual(1, fakeReminderSender.SentMessages.Count, "TaskReminderService sender is not called again after reminder is marked sent");
    dbContext.AgentTasks.RemoveRange(dueReminderTask, completedDueReminderTask);
    await dbContext.SaveChangesAsync(CancellationToken.None);

    CommandResult helpResult = await commandRouter.TryHandleAsync(TextMessage("/help"), testUser, dbContext, CancellationToken.None);
    AssertTrue(helpResult.Handled, "/help is handled");
    AssertTrue(helpResult.ReplyText?.Contains("/remember") == true, "/help lists memory commands");
    AssertTrue(helpResult.ReplyMarkup is null, "/help has no inline keyboard markup by default");

    InlineKeyboardMarkup samplePendingMarkup = InlineKeyboardFactory.ForPendingAction(123);
    AssertTrue(samplePendingMarkup.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Approve" && button.CallbackData == "act:approve:123"), "InlineKeyboardFactory creates approve button");
    AssertTrue(samplePendingMarkup.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Deny" && button.CallbackData == "act:deny:123"), "InlineKeyboardFactory creates deny button");
    AssertTrue(samplePendingMarkup.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Details" && button.CallbackData == "act:details:123"), "InlineKeyboardFactory creates details button");

    string publishTestRoot = Path.Combine(Path.GetTempPath(), $"TelegramMessagingTool_Publish_{Guid.NewGuid():N}");
    string publishTestProjectDir = Path.Combine(publishTestRoot, "TelegramMessagingTool");
    Directory.CreateDirectory(publishTestProjectDir);
    await File.WriteAllTextAsync(Path.Combine(publishTestProjectDir, "TelegramMessagingTool.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""", CancellationToken.None);
    await File.WriteAllTextAsync(Path.Combine(publishTestProjectDir, "Program.cs"), "Console.WriteLine(\"publish fixture\");", CancellationToken.None);
    BotSettings safeCommandAdminSettings = adminTestSettings with
    {
        EnableSafeCommandTools = true,
        SafeCommandProjectRoot = publishTestRoot
    };
    ToolRegistry approvalToolRegistry = ToolRegistryFactory.Create(safeCommandAdminSettings, new HttpClient(), pendingActionService);
    AssertTrue(approvalToolRegistry.TryGet("publish_release", out IAgentTool? publishReleaseTool), "ToolRegistryFactory includes publish_release approval request tool when pending service is provided");
    AssertTrue(approvalToolRegistry.TryGet("restart_latest_bot", out IAgentTool? restartLatestBotTool), "ToolRegistryFactory includes restart_latest_bot approval request tool when pending service is provided");
    AssertTrue(publishReleaseTool!.RequiresApproval, "publish_release requires approval");
    AssertTrue(restartLatestBotTool!.RequiresApproval, "restart_latest_bot requires approval");

    var releaseRequestChatClient = new ScriptedChatClient([
        "{\"type\":\"tool_call\",\"tool\":\"publish_release\",\"input\":\"{\\\"reason\\\":\\\"verify latest changes\\\"}\"}"
    ]);
    var releaseRequestRunner = new AgentRunner(
        releaseRequestChatClient,
        approvalToolRegistry,
        searchRoutingClassifier: new OffSearchRoutingClassifier());
    string releaseRequestReply = await releaseRequestRunner.RunAsync(
        [new OllamaMessageDto("user", "request a publish release approval")],
        CancellationToken.None,
        dbContext,
        testUser);
    AssertTrue(releaseRequestReply.Contains("Pending action #"), "AgentRunner creates a pending action for approval request tools");
    PendingAction publishReleaseAction = await dbContext.PendingActions.OrderByDescending(x => x.Id).FirstAsync(x => x.ToolName == "publish_release", CancellationToken.None);
    AssertEqual(PendingActionStatuses.Pending, publishReleaseAction.Status, "publish_release request is stored as pending");
    AssertTrue(publishReleaseAction.PayloadJson.Contains("verify latest changes"), "publish_release request stores the reason in payload JSON");

    PendingActionDecision publishApproval = await pendingActionService.ApproveAsync(dbContext, testUser, publishReleaseAction.Id, CancellationToken.None);
    AssertTrue(publishApproval.Success, "publish_release pending action can be approved");
    PendingActionExecutionResult publishExecution = await pendingActionExecutor.ExecuteApprovedAsync(dbContext, publishApproval.Action!, CancellationToken.None);
    AssertTrue(publishExecution.Executed, "publish_release approval executes automatically");
    AssertTrue(publishExecution.Success, "publish_release approval publishes successfully");
    string latestReleaseFile = Path.Combine(publishTestRoot, ".latest-release");
    AssertTrue(File.Exists(latestReleaseFile), "publish_release writes .latest-release after successful publish");
    string latestReleasePath = await File.ReadAllTextAsync(latestReleaseFile, CancellationToken.None);
    AssertTrue(latestReleasePath.Contains("release", StringComparison.OrdinalIgnoreCase), "publish_release stores a release folder path");
    AssertTrue(Directory.Exists(Path.Combine(publishTestRoot, latestReleasePath)), "publish_release creates the timestamped release directory");
    AssertTrue(publishExecution.Message.Contains(latestReleasePath, StringComparison.OrdinalIgnoreCase), "publish_release execution records the release path in the result");

    var restartRequestChatClient = new ScriptedChatClient([
        "{\"type\":\"tool_call\",\"tool\":\"restart_latest_bot\",\"input\":\"{\\\"reason\\\":\\\"verify restarted latest release\\\"}\"}"
    ]);
    string restartRequestReply = await new AgentRunner(
        restartRequestChatClient,
        approvalToolRegistry,
        searchRoutingClassifier: new OffSearchRoutingClassifier()).RunAsync(
            [new OllamaMessageDto("user", "request a restart latest bot approval")],
            CancellationToken.None,
            dbContext,
            testUser);
    AssertTrue(restartRequestReply.Contains("Pending action #"), "AgentRunner creates a pending action for restart_latest_bot");
    PendingAction restartLatestAction = await dbContext.PendingActions.OrderByDescending(x => x.Id).FirstAsync(x => x.ToolName == "restart_latest_bot", CancellationToken.None);
    AssertEqual(PendingActionStatuses.Pending, restartLatestAction.Status, "restart_latest_bot request is stored as pending");
    AssertTrue(restartLatestAction.PayloadJson.Contains("verify restarted latest release"), "restart_latest_bot request stores the reason in payload JSON");

    PendingActionDecision restartApproval = await pendingActionService.ApproveAsync(dbContext, testUser, restartLatestAction.Id, CancellationToken.None);
    AssertTrue(restartApproval.Success, "restart_latest_bot pending action can be approved");
    PendingActionExecutionResult restartExecution = await pendingActionExecutor.ExecuteApprovedAsync(dbContext, restartApproval.Action!, CancellationToken.None);
    AssertTrue(restartExecution.Executed, "restart_latest_bot approval executes automatically");
    AssertTrue(restartExecution.Success, "restart_latest_bot approval schedules a restart successfully");
    AssertEqual(publishTestRoot, fakeLatestReleaseRestarter.LastProjectRoot, "restart_latest_bot passes the project root to the restarter");
    AssertEqual(1, fakeLatestReleaseRestarter.RestartCallCount, "restart_latest_bot calls the latest-release restarter once");
    AssertTrue((await dbContext.PendingActions.FindAsync([restartLatestAction.Id], CancellationToken.None))!.DecisionNote.Contains("restart", StringComparison.OrdinalIgnoreCase), "restart_latest_bot records execution result in DecisionNote");
    TestDirectoryCleanup.DeleteRecursive(publishTestRoot);

    string repoWriteRoot = Path.Combine(Path.GetTempPath(), $"TelegramMessagingTool_RepoWrite_{Guid.NewGuid():N}");
    Directory.CreateDirectory(repoWriteRoot);
    await TestProcessRunner.RunAsync("git", ["init"], repoWriteRoot, CancellationToken.None);
    string repoWriteFile = Path.Combine(repoWriteRoot, "SampleFeature.cs");
    await File.WriteAllTextAsync(repoWriteFile, "public class SampleFeature { public string Name => \"Old\"; }\n", CancellationToken.None);
    BotSettings repoWriteSettings = adminTestSettings with
    {
        EnableRepoWriteTools = true,
        SafeCommandProjectRoot = repoWriteRoot
    };
    ToolRegistry repoApprovalRegistry = ToolRegistryFactory.Create(repoWriteSettings, new HttpClient(), pendingActionService);
    var repoWriteChatClient = new ScriptedChatClient([
        "{\"type\":\"tool_call\",\"tool\":\"repo_replace_text\",\"input\":\"{\\\"path\\\":\\\"SampleFeature.cs\\\",\\\"old_text\\\":\\\"Old\\\",\\\"new_text\\\":\\\"New\\\",\\\"reason\\\":\\\"rename sample feature marker\\\"}\"}"
    ]);
    string repoWriteReply = await new AgentRunner(
        repoWriteChatClient,
        repoApprovalRegistry,
        searchRoutingClassifier: new OffSearchRoutingClassifier()).RunAsync(
            [new OllamaMessageDto("user", "request a repository text replacement")],
            CancellationToken.None,
            dbContext,
            testUser);
    AssertTrue(repoWriteReply.Contains("Pending action #"), "AgentRunner creates a pending action for repo_replace_text");
    PendingAction repoReplaceAction = await dbContext.PendingActions.OrderByDescending(x => x.Id).FirstAsync(x => x.ToolName == "repo_replace_text", CancellationToken.None);
    AssertEqual(PendingActionStatuses.Pending, repoReplaceAction.Status, "repo_replace_text request is stored as pending");
    AssertTrue(repoReplaceAction.PayloadJson.Contains("SampleFeature.cs"), "repo_replace_text request stores the relative path");
    AssertTrue((await File.ReadAllTextAsync(repoWriteFile, CancellationToken.None)).Contains("Old"), "repo_replace_text request does not edit before approval");

    PendingActionDecision repoWriteApproval = await pendingActionService.ApproveAsync(dbContext, testUser, repoReplaceAction.Id, CancellationToken.None);
    AssertTrue(repoWriteApproval.Success, "repo_replace_text pending action can be approved");
    PendingActionExecutionResult repoWriteExecution = await pendingActionExecutor.ExecuteApprovedAsync(dbContext, repoWriteApproval.Action!, CancellationToken.None);
    AssertTrue(repoWriteExecution.Executed, "repo_replace_text approval executes automatically");
    AssertTrue(repoWriteExecution.Success, "repo_replace_text execution succeeds");
    string updatedRepoWriteFile = await File.ReadAllTextAsync(repoWriteFile, CancellationToken.None);
    AssertTrue(updatedRepoWriteFile.Contains("New"), "repo_replace_text replaces old text after approval");
    AssertFalse(updatedRepoWriteFile.Contains("Old"), "repo_replace_text removes the old text after approval");

    var directRepoReplaceTool = new RepoReplaceTextRequestTool(pendingActionService, repoWriteSettings, repoWriteRoot);
    ToolResult traversalRepoReplaceResult = await directRepoReplaceTool.CreatePendingActionAsync(
        "{\"path\":\"../outside.cs\",\"old_text\":\"x\",\"new_text\":\"y\"}",
        dbContext,
        testUser,
        CancellationToken.None);
    AssertFalse(traversalRepoReplaceResult.Success, "repo_replace_text rejects path traversal before creating pending actions");
    ToolResult nonAdminRepoReplaceResult = await directRepoReplaceTool.CreatePendingActionAsync(
        "{\"path\":\"SampleFeature.cs\",\"old_text\":\"New\",\"new_text\":\"Next\"}",
        dbContext,
        nonAdminUser,
        CancellationToken.None);
    AssertFalse(nonAdminRepoReplaceResult.Success, "repo_replace_text requires admin before creating pending actions");

    AssertTrue(repoApprovalRegistry.TryGet("repo_apply_patch", out IAgentTool? repoApplyPatchTool), "ToolRegistryFactory includes repo_apply_patch when repo write tools are enabled");
    AssertTrue(repoApplyPatchTool!.RequiresApproval, "repo_apply_patch requires approval");
    string safePatch = """
diff --git a/SampleFeature.cs b/SampleFeature.cs
--- a/SampleFeature.cs
+++ b/SampleFeature.cs
@@ -1 +1 @@
-public class SampleFeature { public string Name => "New"; }
+public class SampleFeature { public string Name => "Patched"; }
""";
    var directRepoApplyPatchTool = new RepoApplyPatchRequestTool(pendingActionService, repoWriteSettings, repoWriteRoot);
    ToolResult repoApplyPatchRequest = await directRepoApplyPatchTool.CreatePendingActionAsync(
        JsonSerializer.Serialize(new { patch = safePatch, reason = "apply safe source patch" }),
        dbContext,
        testUser,
        CancellationToken.None);
    AssertTrue(repoApplyPatchRequest.Success, "repo_apply_patch creates a pending action for a safe unified diff");
    PendingAction repoApplyPatchAction = await dbContext.PendingActions.OrderByDescending(x => x.Id).FirstAsync(x => x.ToolName == "repo_apply_patch", CancellationToken.None);
    AssertEqual(PendingActionStatuses.Pending, repoApplyPatchAction.Status, "repo_apply_patch request is stored as pending");
    AssertTrue(repoApplyPatchAction.PayloadJson.Contains("SampleFeature.cs"), "repo_apply_patch request stores affected paths in payload JSON");
    AssertTrue((await File.ReadAllTextAsync(repoWriteFile, CancellationToken.None)).Contains("New"), "repo_apply_patch request does not edit before approval");

    PendingActionDecision repoApplyPatchApproval = await pendingActionService.ApproveAsync(dbContext, testUser, repoApplyPatchAction.Id, CancellationToken.None);
    AssertTrue(repoApplyPatchApproval.Success, "repo_apply_patch pending action can be approved");
    PendingActionExecutionResult repoApplyPatchExecution = await pendingActionExecutor.ExecuteApprovedAsync(dbContext, repoApplyPatchApproval.Action!, CancellationToken.None);
    AssertTrue(repoApplyPatchExecution.Executed, "repo_apply_patch approval executes automatically");
    AssertTrue(repoApplyPatchExecution.Success, $"repo_apply_patch execution succeeds for a valid unified diff: {repoApplyPatchExecution.Message}");
    string patchedRepoWriteFile = await File.ReadAllTextAsync(repoWriteFile, CancellationToken.None);
    AssertTrue(patchedRepoWriteFile.Contains("Patched"), "repo_apply_patch applies the approved diff after approval");
    AssertFalse(patchedRepoWriteFile.Contains("New"), "repo_apply_patch removes the old text after approval");

    string traversalPatch = """
diff --git a/../outside.cs b/../outside.cs
--- a/../outside.cs
+++ b/../outside.cs
@@ -1 +1 @@
-old
+new
""";
    ToolResult traversalRepoApplyPatchResult = await directRepoApplyPatchTool.CreatePendingActionAsync(
        JsonSerializer.Serialize(new { patch = traversalPatch, reason = "try traversal" }),
        dbContext,
        testUser,
        CancellationToken.None);
    AssertFalse(traversalRepoApplyPatchResult.Success, "repo_apply_patch rejects path traversal before creating pending actions");
    ToolResult nonAdminRepoApplyPatchResult = await directRepoApplyPatchTool.CreatePendingActionAsync(
        JsonSerializer.Serialize(new { patch = safePatch, reason = "unauthorized patch" }),
        dbContext,
        nonAdminUser,
        CancellationToken.None);
    AssertFalse(nonAdminRepoApplyPatchResult.Success, "repo_apply_patch requires admin before creating pending actions");
    Directory.Delete(repoWriteRoot, recursive: true);

    string repoCommitRoot = Path.Combine(Path.GetTempPath(), $"TelegramMessagingTool_RepoCommit_{Guid.NewGuid():N}");
    Directory.CreateDirectory(repoCommitRoot);
    await TestProcessRunner.RunAsync("git", ["init"], repoCommitRoot, CancellationToken.None);
    await TestProcessRunner.RunAsync("git", ["config", "user.email", "test@example.local"], repoCommitRoot, CancellationToken.None);
    await TestProcessRunner.RunAsync("git", ["config", "user.name", "TelegramMessagingTool Tests"], repoCommitRoot, CancellationToken.None);
    string repoCommitFile = Path.Combine(repoCommitRoot, "TrackedFile.md");
    await File.WriteAllTextAsync(repoCommitFile, "# Initial\n", CancellationToken.None);
    await TestProcessRunner.RunAsync("git", ["add", "TrackedFile.md"], repoCommitRoot, CancellationToken.None);
    await TestProcessRunner.RunAsync("git", ["commit", "-m", "Initial commit"], repoCommitRoot, CancellationToken.None);
    await File.WriteAllTextAsync(repoCommitFile, "# Updated\n", CancellationToken.None);
    BotSettings repoCommitSettings = adminTestSettings with
    {
        EnableRepoWriteTools = true,
        SafeCommandProjectRoot = repoCommitRoot
    };
    ToolRegistry repoCommitRegistry = ToolRegistryFactory.Create(repoCommitSettings, new HttpClient(), pendingActionService);
    var repoCommitChatClient = new ScriptedChatClient([
        "{\"type\":\"tool_call\",\"tool\":\"repo_commit_changes\",\"input\":\"{\\\"message\\\":\\\"Update tracked file\\\",\\\"body\\\":\\\"Commits approved repo edit.\\\"}\"}"
    ]);
    string repoCommitReply = await new AgentRunner(
        repoCommitChatClient,
        repoCommitRegistry,
        searchRoutingClassifier: new OffSearchRoutingClassifier()).RunAsync(
            [new OllamaMessageDto("user", "request a commit for the approved repo edit")],
            CancellationToken.None,
            dbContext,
            testUser);
    AssertTrue(repoCommitReply.Contains("Pending action #"), "AgentRunner creates a pending action for repo_commit_changes");
    PendingAction repoCommitAction = await dbContext.PendingActions.OrderByDescending(x => x.Id).FirstAsync(x => x.ToolName == "repo_commit_changes", CancellationToken.None);
    AssertEqual(PendingActionStatuses.Pending, repoCommitAction.Status, "repo_commit_changes request is stored as pending");
    AssertTrue(repoCommitAction.PayloadJson.Contains("Update tracked file"), "repo_commit_changes request stores the commit message");

    PendingActionDecision repoCommitApproval = await pendingActionService.ApproveAsync(dbContext, testUser, repoCommitAction.Id, CancellationToken.None);
    AssertTrue(repoCommitApproval.Success, "repo_commit_changes pending action can be approved");
    PendingActionExecutionResult repoCommitExecution = await pendingActionExecutor.ExecuteApprovedAsync(dbContext, repoCommitApproval.Action!, CancellationToken.None);
    AssertTrue(repoCommitExecution.Executed, "repo_commit_changes approval executes automatically");
    AssertTrue(repoCommitExecution.Success, "repo_commit_changes execution succeeds when there is a diff");
    ProcessRunResult commitLog = await TestProcessRunner.RunAsync("git", ["log", "-1", "--pretty=%s"], repoCommitRoot, CancellationToken.None);
    AssertTrue(commitLog.Output.Contains("Update tracked file"), "repo_commit_changes creates a commit with the requested message");
    ProcessRunResult commitStatus = await TestProcessRunner.RunAsync("git", ["status", "--short"], repoCommitRoot, CancellationToken.None);
    AssertEqual(string.Empty, commitStatus.Output.Trim(), "repo_commit_changes leaves the temp repo clean after commit");

    var directRepoCommitTool = new RepoCommitChangesRequestTool(pendingActionService, repoCommitSettings, repoCommitRoot);
    ToolResult invalidRepoCommitMessageResult = await directRepoCommitTool.CreatePendingActionAsync(
        "{\"message\":\"\"}",
        dbContext,
        testUser,
        CancellationToken.None);
    AssertFalse(invalidRepoCommitMessageResult.Success, "repo_commit_changes rejects empty commit messages");
    ToolResult nonAdminRepoCommitResult = await directRepoCommitTool.CreatePendingActionAsync(
        "{\"message\":\"Unauthorized commit\"}",
        dbContext,
        nonAdminUser,
        CancellationToken.None);
    AssertFalse(nonAdminRepoCommitResult.Success, "repo_commit_changes requires admin before creating pending actions");
    PendingAction emptyDiffCommitAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "repo_commit_changes",
        "Commit with no diff.",
        "{\"action\":\"repo_commit_changes\",\"projectRoot\":\"" + JsonEncodedText.Encode(repoCommitRoot).ToString() + "\",\"message\":\"No diff commit\",\"body\":\"\"}",
        "high",
        TimeSpan.FromMinutes(15),
        CancellationToken.None);
    PendingActionDecision emptyDiffCommitApproval = await pendingActionService.ApproveAsync(dbContext, testUser, emptyDiffCommitAction.Id, CancellationToken.None);
    PendingActionExecutionResult emptyDiffCommitExecution = await pendingActionExecutor.ExecuteApprovedAsync(dbContext, emptyDiffCommitApproval.Action!, CancellationToken.None);
    AssertTrue(emptyDiffCommitExecution.Executed, "repo_commit_changes empty-diff action is handled by executor");
    AssertFalse(emptyDiffCommitExecution.Success, "repo_commit_changes refuses to commit when there is no diff");

    string repoPushRemote = Path.Combine(Path.GetTempPath(), $"TelegramMessagingTool_RepoPushRemote_{Guid.NewGuid():N}.git");
    await TestProcessRunner.RunAsync("git", ["init", "--bare", repoPushRemote], Path.GetTempPath(), CancellationToken.None);
    await TestProcessRunner.RunAsync("git", ["remote", "add", "origin", repoPushRemote], repoCommitRoot, CancellationToken.None);
    await TestProcessRunner.RunAsync("git", ["push", "-u", "origin", "master"], repoCommitRoot, CancellationToken.None);
    await File.WriteAllTextAsync(repoCommitFile, "# Pushed update\n", CancellationToken.None);
    await TestProcessRunner.RunAsync("git", ["add", "TrackedFile.md"], repoCommitRoot, CancellationToken.None);
    await TestProcessRunner.RunAsync("git", ["commit", "-m", "Prepare push test"], repoCommitRoot, CancellationToken.None);

    var repoPushChatClient = new ScriptedChatClient([
        "{\"type\":\"tool_call\",\"tool\":\"repo_push_changes\",\"input\":\"{\\\"reason\\\":\\\"push approved commit to origin\\\"}\"}"
    ]);
    string repoPushReply = await new AgentRunner(
        repoPushChatClient,
        repoCommitRegistry,
        searchRoutingClassifier: new OffSearchRoutingClassifier()).RunAsync(
            [new OllamaMessageDto("user", "request a push for the approved repo commit")],
            CancellationToken.None,
            dbContext,
            testUser);
    AssertTrue(repoPushReply.Contains("Pending action #"), "AgentRunner creates a pending action for repo_push_changes");
    PendingAction repoPushAction = await dbContext.PendingActions.OrderByDescending(x => x.Id).FirstAsync(x => x.ToolName == "repo_push_changes", CancellationToken.None);
    AssertEqual(PendingActionStatuses.Pending, repoPushAction.Status, "repo_push_changes request is stored as pending");
    AssertTrue(repoPushAction.PayloadJson.Contains("push approved commit"), "repo_push_changes request stores the push reason");

    PendingActionDecision repoPushApproval = await pendingActionService.ApproveAsync(dbContext, testUser, repoPushAction.Id, CancellationToken.None);
    AssertTrue(repoPushApproval.Success, "repo_push_changes pending action can be approved");
    PendingActionExecutionResult repoPushExecution = await pendingActionExecutor.ExecuteApprovedAsync(dbContext, repoPushApproval.Action!, CancellationToken.None);
    AssertTrue(repoPushExecution.Executed, "repo_push_changes approval executes automatically");
    AssertTrue(repoPushExecution.Success, "repo_push_changes execution succeeds for clean repo with local origin");
    ProcessRunResult remoteLog = await TestProcessRunner.RunAsync("git", ["--git-dir", repoPushRemote, "log", "-1", "--pretty=%s"], Path.GetTempPath(), CancellationToken.None);
    AssertTrue(remoteLog.Output.Contains("Prepare push test"), "repo_push_changes pushes the current branch to origin");

    var directRepoPushTool = new RepoPushChangesRequestTool(pendingActionService, repoCommitSettings, repoCommitRoot);
    ToolResult nonAdminRepoPushResult = await directRepoPushTool.CreatePendingActionAsync(
        "{\"reason\":\"Unauthorized push\"}",
        dbContext,
        nonAdminUser,
        CancellationToken.None);
    AssertFalse(nonAdminRepoPushResult.Success, "repo_push_changes requires admin before creating pending actions");
    await File.WriteAllTextAsync(repoCommitFile, "# Dirty working tree\n", CancellationToken.None);
    PendingAction dirtyRepoPushAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "repo_push_changes",
        "Push with dirty tree.",
        "{\"action\":\"repo_push_changes\",\"projectRoot\":\"" + JsonEncodedText.Encode(repoCommitRoot).ToString() + "\",\"reason\":\"dirty tree push\"}",
        "high",
        TimeSpan.FromMinutes(15),
        CancellationToken.None);
    PendingActionDecision dirtyRepoPushApproval = await pendingActionService.ApproveAsync(dbContext, testUser, dirtyRepoPushAction.Id, CancellationToken.None);
    PendingActionExecutionResult dirtyRepoPushExecution = await pendingActionExecutor.ExecuteApprovedAsync(dbContext, dirtyRepoPushApproval.Action!, CancellationToken.None);
    AssertTrue(dirtyRepoPushExecution.Executed, "repo_push_changes dirty-tree action is handled by executor");
    AssertFalse(dirtyRepoPushExecution.Success, "repo_push_changes refuses to push when the working tree is dirty");
    TestDirectoryCleanup.DeleteRecursive(repoCommitRoot);
    TestDirectoryCleanup.DeleteRecursive(repoPushRemote);

    AssertTrue(PendingActionCallbackParser.TryParse("act:approve:123", out PendingActionCallback approveCallback), "PendingActionCallbackParser parses approve callback");
    AssertEqual(PendingActionCallbackVerb.Approve, approveCallback.Verb, "PendingActionCallbackParser reads approve verb");
    AssertEqual(123, approveCallback.ActionId, "PendingActionCallbackParser reads approve action id");
    AssertTrue(PendingActionCallbackParser.TryParse("act:deny:456", out PendingActionCallback denyCallback), "PendingActionCallbackParser parses deny callback");
    AssertEqual(PendingActionCallbackVerb.Deny, denyCallback.Verb, "PendingActionCallbackParser reads deny verb");
    AssertEqual(456, denyCallback.ActionId, "PendingActionCallbackParser reads deny action id");
    AssertTrue(PendingActionCallbackParser.TryParse("act:details:789", out PendingActionCallback detailsCallback), "PendingActionCallbackParser parses details callback");
    AssertEqual(PendingActionCallbackVerb.Details, detailsCallback.Verb, "PendingActionCallbackParser reads details verb");
    AssertEqual(789, detailsCallback.ActionId, "PendingActionCallbackParser reads details action id");
    AssertFalse(PendingActionCallbackParser.TryParse("/pending", out _), "PendingActionCallbackParser rejects slash commands");
    AssertFalse(PendingActionCallbackParser.TryParse("approve:123", out _), "PendingActionCallbackParser rejects missing domain prefix");
    AssertFalse(PendingActionCallbackParser.TryParse("act:approve:nope", out _), "PendingActionCallbackParser rejects non-numeric action ids");
    AssertFalse(PendingActionCallbackParser.TryParse("act:unknown:123", out _), "PendingActionCallbackParser rejects unknown verbs");
    AssertFalse(PendingActionCallbackParser.TryParse("act:approve", out _), "PendingActionCallbackParser rejects missing id");
    AssertFalse(PendingActionCallbackParser.TryParse("act:approve:123:extra", out _), "PendingActionCallbackParser rejects extra callback parts");

    CommandResult statusResult = await commandRouter.TryHandleAsync(TextMessage("/status"), testUser, dbContext, CancellationToken.None);
    AssertTrue(statusResult.Handled, "/status is handled");
    AssertTrue(statusResult.ReplyText?.Contains("Database: OK") == true, "/status reports database OK");
    AssertTrue(statusResult.ReplyText?.Contains("Access mode: admin-only") == true, "/status reports access mode");
    AssertTrue(statusResult.ReplyText?.Contains("Model routes:") == true, "/status reports model routes heading");
    AssertTrue(statusResult.ReplyText?.Contains("plan=qwen3:4b") == true, "/status reports planning model route");
    AssertTrue(statusResult.ReplyText?.Contains("doc_qa=qwen3:8b") == true, "/status reports document QA model route");
    AssertTrue(statusResult.ReplyText?.Contains("Search routing: heuristic") == true, "/status reports search routing mode");
    AssertTrue(statusResult.ReplyText?.Contains("Image vision: disabled") == true, "/status reports image vision mode");
    AssertTrue(statusResult.ReplyText?.Contains("Image prompt: default") == true, "/status reports image prompt mode");
    AssertTrue(statusResult.ReplyText?.Contains("Audio provider: not configured") == true, "/status reports audio transcription provider mode");
    AssertTrue(statusResult.ReplyText?.Contains("Text-to-speech: disabled") == true, "/status reports TTS gate mode");
    AssertTrue(statusResult.ReplyText?.Contains("TTS provider: not configured") == true, "/status reports TTS provider mode");
    AssertTrue(statusResult.ReplyText?.Contains("Safe command tools: disabled") == true, "/status reports safe command tools mode");

    CommandResult statusMentionResult = await commandRouter.TryHandleAsync(TextMessage("/status@red_eye_ghost_bot"), testUser, dbContext, CancellationToken.None);
    AssertTrue(statusMentionResult.Handled, "/status@bot is handled");
    AssertTrue(statusMentionResult.ReplyText?.Contains("Database: OK") == true, "/status@bot reports database OK");

    CommandResult statusPrefixResult = await commandRouter.TryHandleAsync(TextMessage("/statusx"), testUser, dbContext, CancellationToken.None);
    AssertFalse(statusPrefixResult.Handled, "/statusx is not treated as /status");

    CommandResult healthResult = await commandRouter.TryHandleAsync(TextMessage("/health"), testUser, dbContext, CancellationToken.None);
    AssertTrue(healthResult.Handled, "/health is handled");
    AssertTrue(healthResult.ReplyText?.Contains("Health", StringComparison.OrdinalIgnoreCase) == true, "/health reports heading");
    AssertTrue(healthResult.ReplyText?.Contains("Database: OK", StringComparison.OrdinalIgnoreCase) == true, "/health reports database OK");
    AssertTrue(healthResult.ReplyText?.Contains("Uptime:", StringComparison.OrdinalIgnoreCase) == true, "/health reports process uptime");
    AssertTrue(healthResult.ReplyText?.Contains("Model routes:", StringComparison.OrdinalIgnoreCase) == true, "/health reports model route summary");
    AssertTrue(healthResult.ReplyText?.Contains("Search routing: heuristic", StringComparison.OrdinalIgnoreCase) == true, "/health reports search routing mode");
    AssertTrue(healthResult.ReplyText?.Contains("Plugins:", StringComparison.OrdinalIgnoreCase) == true, "/health reports plugin status");
    AssertTrue(healthResult.ReplyText?.Contains("Document storage:", StringComparison.OrdinalIgnoreCase) == true, "/health reports document storage status");
    AssertTrue(healthResult.ReplyText?.Contains("Import inbox:", StringComparison.OrdinalIgnoreCase) == true, "/health reports import inbox status");
    AssertTrue(healthResult.ReplyText?.Contains("Risk warnings:", StringComparison.OrdinalIgnoreCase) == true, "/health reports risk warning count");
    AssertFalse(healthResult.ReplyText?.Contains("test-token", StringComparison.OrdinalIgnoreCase) == true, "/health does not render bot token");
    AssertFalse(healthResult.ReplyText?.Contains("ghp_secret", StringComparison.OrdinalIgnoreCase) == true, "/health does not render GitHub token markers");
    CommandResult healthMentionResult = await commandRouter.TryHandleAsync(TextMessage("/health@red_eye_ghost_bot"), testUser, dbContext, CancellationToken.None);
    AssertTrue(healthMentionResult.Handled, "/health@bot is handled");
    AssertFalse((await commandRouter.TryHandleAsync(TextMessage("/healthx"), testUser, dbContext, CancellationToken.None)).Handled, "/healthx is not treated as /health");

    CommandResult errorsResult = await commandRouter.TryHandleAsync(TextMessage("/errors 2"), testUser, dbContext, CancellationToken.None);
    AssertTrue(errorsResult.Handled, "/errors is handled");
    AssertTrue(errorsResult.ReplyText?.Contains("Recent runtime warnings/errors", StringComparison.OrdinalIgnoreCase) == true, "/errors reports heading");
    AssertTrue(errorsResult.ReplyText?.Contains("SEARCH", StringComparison.OrdinalIgnoreCase) == true, "/errors includes recent warning category");
    AssertTrue(errorsResult.ReplyText?.Contains("TELEGRAM", StringComparison.OrdinalIgnoreCase) == true, "/errors includes recent error category");
    AssertFalse(errorsResult.ReplyText?.Contains("COMMAND", StringComparison.OrdinalIgnoreCase) == true, "/errors filters info events");
    AssertFalse(errorsResult.ReplyText?.Contains("TOKEN=123456", StringComparison.OrdinalIgnoreCase) == true, "/errors redacts token assignments");
    AssertFalse(errorsResult.ReplyText?.Contains("ghp_secret", StringComparison.OrdinalIgnoreCase) == true, "/errors redacts GitHub tokens");
    CommandResult errorsDefaultResult = await commandRouter.TryHandleAsync(TextMessage("/errors"), testUser, dbContext, CancellationToken.None);
    AssertTrue(errorsDefaultResult.Handled, "/errors without count is handled");
    AssertTrue(errorsDefaultResult.ReplyText?.Contains("showing 3", StringComparison.OrdinalIgnoreCase) == true, "/errors defaults to available recent warnings/errors");
    CommandResult errorsClampResult = await commandRouter.TryHandleAsync(TextMessage("/errors 999"), testUser, dbContext, CancellationToken.None);
    AssertTrue(errorsClampResult.ReplyText?.Contains("limit 50", StringComparison.OrdinalIgnoreCase) == true, "/errors clamps high count to 50");
    CommandResult nonAdminErrorsResult = await commandRouter.TryHandleAsync(TextMessage("/errors"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminErrorsResult.Handled, "/errors non-admin attempt is handled");
    AssertTrue(nonAdminErrorsResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/errors requires admin");
    CommandResult errorsMentionResult = await commandRouter.TryHandleAsync(TextMessage("/errors@red_eye_ghost_bot 1"), testUser, dbContext, CancellationToken.None);
    AssertTrue(errorsMentionResult.Handled, "/errors@bot is handled");
    AssertFalse((await commandRouter.TryHandleAsync(TextMessage("/errorsx"), testUser, dbContext, CancellationToken.None)).Handled, "/errorsx is not treated as /errors");

    CommandResult toolsResult = await commandRouter.TryHandleAsync(TextMessage("/tools"), testUser, dbContext, CancellationToken.None);
    AssertTrue(toolsResult.Handled, "/tools is handled");
    AssertTrue(toolsResult.ReplyText?.Contains("online_search") == true, "/tools lists online search");

    CommandResult systemInfoResult = await commandRouter.TryHandleAsync(TextMessage("/systeminfo"), testUser, dbContext, CancellationToken.None);
    AssertTrue(systemInfoResult.Handled, "/systeminfo is handled");
    AssertTrue(systemInfoResult.ReplyText?.Contains("Operating system") == true, "/systeminfo reports OS info");

    CommandResult diskStatusResult = await commandRouter.TryHandleAsync(TextMessage("/diskstatus"), testUser, dbContext, CancellationToken.None);
    AssertTrue(diskStatusResult.Handled, "/diskstatus is handled");
    AssertTrue(diskStatusResult.ReplyText?.Contains("Disk status") == true, "/diskstatus reports disk info");

    CommandResult processesResult = await commandRouter.TryHandleAsync(TextMessage("/processes"), testUser, dbContext, CancellationToken.None);
    AssertTrue(processesResult.Handled, "/processes is handled");
    AssertTrue(processesResult.ReplyText?.Contains("Running processes") == true, "/processes reports process info");

    CommandResult invalidKillProcessResult = await commandRouter.TryHandleAsync(TextMessage("/killprocess nope"), testUser, dbContext, CancellationToken.None);
    AssertTrue(invalidKillProcessResult.Handled, "/killprocess invalid input is handled");
    AssertTrue(invalidKillProcessResult.ReplyText?.Contains("Usage: /killprocess <pid>") == true, "/killprocess validates PID input");

    CommandResult nonAdminKillProcessResult = await commandRouter.TryHandleAsync(TextMessage("/killprocess 12345"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminKillProcessResult.Handled, "/killprocess non-admin attempt is handled");
    AssertTrue(nonAdminKillProcessResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/killprocess requires admin");
    AssertEqual(0, await dbContext.PendingActions.CountAsync(x => x.ConnectedUserId == nonAdminUser.Id), "/killprocess non-admin does not create pending action");

    CommandResult killProcessResult = await commandRouter.TryHandleAsync(TextMessage("/killprocess 12345"), testUser, dbContext, CancellationToken.None);
    AssertTrue(killProcessResult.Handled, "/killprocess is handled");
    AssertTrue(killProcessResult.ReplyText?.Contains("approval", StringComparison.OrdinalIgnoreCase) == true, "/killprocess asks for approval instead of executing");
    PendingAction killProcessPendingAction = await dbContext.PendingActions.SingleAsync(x => x.ToolName == "kill_process", CancellationToken.None);
    AssertEqual("high", killProcessPendingAction.RiskLevel, "/killprocess creates high risk pending action");
    AssertTrue(killProcessPendingAction.PayloadJson.Contains("12345"), "/killprocess stores target PID in payload");

    CommandResult approveKillProcessResult = await commandRouter.TryHandleAsync(TextMessage($"/approve {killProcessPendingAction.Id}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(approveKillProcessResult.Handled, "/approve kill_process is handled");
    AssertEqual("✅", approveKillProcessResult.ReactionEmoji, "/approve requests success reaction metadata");
    AssertTrue(approveKillProcessResult.ReplyText?.Contains("Execution result", StringComparison.OrdinalIgnoreCase) == true, "/approve kill_process reports execution result");
    AssertEqual(12345, fakeProcessTerminator.LastRequestedPid, "/approve kill_process executes approved PID through safe terminator");
    AssertEqual(1, fakeProcessTerminator.KillCallCount, "/approve kill_process executes once");
    AssertTrue((await dbContext.PendingActions.FindAsync([killProcessPendingAction.Id], CancellationToken.None))!.DecisionNote.Contains("terminated", StringComparison.OrdinalIgnoreCase), "/approve kill_process records execution result");

    CommandResult invalidActionResult = await commandRouter.TryHandleAsync(TextMessage("/action nope"), testUser, dbContext, CancellationToken.None);
    AssertTrue(invalidActionResult.Handled, "/action invalid input is handled");
    AssertTrue(invalidActionResult.ReplyText?.Contains("Usage: /action <pending-action-id>") == true, "/action validates action id input");

    CommandResult actionDetailsResult = await commandRouter.TryHandleAsync(TextMessage($"/action {killProcessPendingAction.Id}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(actionDetailsResult.Handled, "/action is handled");
    AssertTrue(actionDetailsResult.ReplyText?.Contains($"Action #{killProcessPendingAction.Id}") == true, "/action shows action id");
    AssertTrue(actionDetailsResult.ReplyText?.Contains("kill_process") == true, "/action shows action type");
    AssertTrue(actionDetailsResult.ReplyText?.Contains("Status: approved") == true, "/action shows action status");
    AssertTrue(actionDetailsResult.ReplyText?.Contains("Decision note") == true, "/action shows decision note");
    AssertTrue(actionDetailsResult.ReplyText?.Contains("Payload") == true, "/action shows payload summary");
    AssertTrue(actionDetailsResult.ReplyText?.Contains("12345") == true, "/action includes target PID payload");

    CommandResult nonAdminActionResult = await commandRouter.TryHandleAsync(TextMessage($"/action {killProcessPendingAction.Id}"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminActionResult.Handled, "/action non-admin attempt is handled");
    AssertTrue(nonAdminActionResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/action requires admin");

    PendingAction pendingAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "project_patch_file",
        "Patch a source file after user approval.",
        "{\"path\":\"Program.cs\"}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);

    CommandResult pendingResult = await commandRouter.TryHandleAsync(TextMessage("/pending"), testUser, dbContext, CancellationToken.None);
    AssertTrue(pendingResult.Handled, "/pending is handled");
    AssertTrue(pendingResult.ReplyText?.Contains($"#{pendingAction.Id}") == true, "/pending lists pending action");
    AssertTrue(pendingResult.ReplyMarkup is not null, "/pending includes inline action keyboard metadata");
    AssertTrue(pendingResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.CallbackData == $"act:approve:{pendingAction.Id}"), "/pending action keyboard includes approve callback");
    AssertTrue(pendingResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.CallbackData == $"act:deny:{pendingAction.Id}"), "/pending action keyboard includes deny callback");

    var pendingCallbackService = new PendingActionCallbackService(pendingActionService, pendingActionExecutor, adminTestSettings);
    PendingActionCallbackResult invalidCallbackResult = await pendingCallbackService.HandleAsync("task:open:1", testUser, testUser.ChatId, dbContext, CancellationToken.None);
    AssertFalse(invalidCallbackResult.Handled, "PendingActionCallbackService ignores non-action callback domains");

    PendingActionCallbackResult detailsCallbackResult = await pendingCallbackService.HandleAsync($"act:details:{pendingAction.Id}", testUser, testUser.ChatId, dbContext, CancellationToken.None);
    AssertTrue(detailsCallbackResult.Handled, "PendingActionCallbackService handles details callbacks");
    AssertTrue(detailsCallbackResult.AnswerText.Contains("Details"), "PendingActionCallbackService answers callback details requests");
    AssertTrue(detailsCallbackResult.MessageText?.Contains($"Action #{pendingAction.Id}") == true, "PendingActionCallbackService details includes action id");
    AssertEqual(PendingActionStatuses.Pending, (await dbContext.PendingActions.FindAsync([pendingAction.Id], CancellationToken.None))!.Status, "PendingActionCallbackService details does not decide action");

    PendingAction callbackDenyAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "callback_deny_test",
        "Deny from inline callback.",
        "{}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);
    PendingActionCallbackResult denyCallbackResult = await pendingCallbackService.HandleAsync($"act:deny:{callbackDenyAction.Id}", testUser, testUser.ChatId, dbContext, CancellationToken.None);
    AssertTrue(denyCallbackResult.Handled, "PendingActionCallbackService handles deny callbacks");
    AssertTrue(denyCallbackResult.MessageText?.Contains("denied", StringComparison.OrdinalIgnoreCase) == true, "PendingActionCallbackService deny returns denial message");
    AssertEqual(PendingActionStatuses.Denied, (await dbContext.PendingActions.FindAsync([callbackDenyAction.Id], CancellationToken.None))!.Status, "PendingActionCallbackService deny marks action denied");

    PendingAction callbackApproveAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "kill_process",
        "Terminate PID 54321 after inline approval.",
        "{\"pid\":54321}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);
    int killCallsBeforeCallback = fakeProcessTerminator.KillCallCount;
    PendingActionCallbackResult approveCallbackResult = await pendingCallbackService.HandleAsync($"act:approve:{callbackApproveAction.Id}", testUser, testUser.ChatId, dbContext, CancellationToken.None);
    AssertTrue(approveCallbackResult.Handled, "PendingActionCallbackService handles approve callbacks");
    AssertTrue(approveCallbackResult.MessageText?.Contains("Execution result", StringComparison.OrdinalIgnoreCase) == true, "PendingActionCallbackService approve reports execution result");
    AssertEqual(54321, fakeProcessTerminator.LastRequestedPid, "PendingActionCallbackService approve executes kill_process through safe terminator");
    AssertEqual(killCallsBeforeCallback + 1, fakeProcessTerminator.KillCallCount, "PendingActionCallbackService approve executes once");
    AssertEqual(PendingActionStatuses.Approved, (await dbContext.PendingActions.FindAsync([callbackApproveAction.Id], CancellationToken.None))!.Status, "PendingActionCallbackService approve marks action approved");

    PendingAction callbackNonAdminAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "callback_admin_test",
        "Admin-only callback action.",
        "{}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);
    PendingActionCallbackResult nonAdminCallbackResult = await pendingCallbackService.HandleAsync($"act:deny:{callbackNonAdminAction.Id}", nonAdminUser, nonAdminUser.ChatId, dbContext, CancellationToken.None);
    AssertTrue(nonAdminCallbackResult.Handled, "PendingActionCallbackService handles non-admin callback attempts");
    AssertTrue(nonAdminCallbackResult.MessageText?.Contains("not authorized", StringComparison.OrdinalIgnoreCase) == true, "PendingActionCallbackService rejects non-admin callback actors");
    AssertEqual(PendingActionStatuses.Pending, (await dbContext.PendingActions.FindAsync([callbackNonAdminAction.Id], CancellationToken.None))!.Status, "PendingActionCallbackService non-admin does not decide action");

    CommandResult nonAdminPendingResult = await commandRouter.TryHandleAsync(TextMessage("/pending"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminPendingResult.Handled, "/pending non-admin attempt is handled");
    AssertTrue(nonAdminPendingResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/pending requires admin");

    CommandResult nonAdminApproveResult = await commandRouter.TryHandleAsync(TextMessage($"/approve {pendingAction.Id}"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminApproveResult.Handled, "/approve non-admin attempt is handled");
    AssertTrue(nonAdminApproveResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/approve requires admin");
    AssertEqual(PendingActionStatuses.Pending, (await dbContext.PendingActions.FindAsync([pendingAction.Id], CancellationToken.None))!.Status, "/approve non-admin does not approve action");

    CommandResult approveResult = await commandRouter.TryHandleAsync(TextMessage($"/approve {pendingAction.Id}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(approveResult.Handled, "/approve is handled");
    AssertEqual(PendingActionStatuses.Approved, (await dbContext.PendingActions.FindAsync([pendingAction.Id], CancellationToken.None))!.Status, "/approve marks action approved");
    AssertTrue(approveResult.ReplyText?.Contains("No automatic execution", StringComparison.OrdinalIgnoreCase) == true, "/approve does not execute unknown action types");
    AssertEqual(killCallsBeforeCallback + 1, fakeProcessTerminator.KillCallCount, "/approve does not call process terminator for non-kill actions");

    PendingAction secondPendingAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "git_push",
        "Push committed changes after approval.",
        "{}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);

    CommandResult denyResult = await commandRouter.TryHandleAsync(TextMessage($"/deny {secondPendingAction.Id}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(denyResult.Handled, "/deny is handled");
    AssertEqual("👎", denyResult.ReactionEmoji, "/deny requests negative reaction metadata");
    AssertEqual(PendingActionStatuses.Denied, (await dbContext.PendingActions.FindAsync([secondPendingAction.Id], CancellationToken.None))!.Status, "/deny marks action denied");

    PendingAction thirdPendingAction = await pendingActionService.CreateAsync(
        dbContext,
        testUser,
        "database_mutation",
        "Mutate a database record after approval.",
        "{}",
        "high",
        TimeSpan.FromMinutes(30),
        CancellationToken.None);

    CommandResult nonAdminDenyResult = await commandRouter.TryHandleAsync(TextMessage($"/deny {thirdPendingAction.Id}"), nonAdminUser, dbContext, CancellationToken.None);
    AssertTrue(nonAdminDenyResult.Handled, "/deny non-admin attempt is handled");
    AssertTrue(nonAdminDenyResult.ReplyText?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true, "/deny requires admin");
    AssertEqual(PendingActionStatuses.Pending, (await dbContext.PendingActions.FindAsync([thirdPendingAction.Id], CancellationToken.None))!.Status, "/deny non-admin does not deny action");

    CommandResult planResult = await commandRouter.TryHandleAsync(TextMessage("/plan build a small inventory API"), testUser, dbContext, CancellationToken.None);
    AssertTrue(planResult.Handled, "/plan is handled");
    AssertEqual(1, await dbContext.AgentTasks.CountAsync(x => x.ConnectedUserId == testUser.Id), "/plan creates task");
    int taskId = await dbContext.AgentTasks.Where(x => x.ConnectedUserId == testUser.Id).Select(x => x.Id).SingleAsync();
    AssertEqual(5, await dbContext.AgentTaskSteps.CountAsync(x => x.AgentTaskId == taskId), "/plan creates default task steps");

    CommandResult tasksResult = await commandRouter.TryHandleAsync(TextMessage("/tasks"), testUser, dbContext, CancellationToken.None);
    AssertTrue(tasksResult.Handled, "/tasks is handled");
    AssertTrue(tasksResult.ReplyText?.Contains("inventory API", StringComparison.OrdinalIgnoreCase) == true, "/tasks lists planned goal");
    AssertTrue(tasksResult.ReplyMarkup is not null, "/tasks includes inline task keyboard metadata");
    AssertTrue(tasksResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Open" && button.CallbackData == $"task:open:{taskId}"), "/tasks keyboard includes open callback for first task");

    AssertTrue(TaskCallbackParser.TryParse($"task:open:{taskId}", out TaskCallback openTaskCallback), "TaskCallbackParser parses open callback");
    AssertEqual(TaskCallbackVerb.Open, openTaskCallback.Verb, "TaskCallbackParser reads open verb");
    AssertEqual(taskId, openTaskCallback.TaskId, "TaskCallbackParser reads open task id");
    AssertTrue(TaskCallbackParser.TryParse($"task:done:{taskId}", out TaskCallback doneTaskCallback), "TaskCallbackParser parses done callback");
    AssertEqual(TaskCallbackVerb.Done, doneTaskCallback.Verb, "TaskCallbackParser reads done verb");
    AssertTrue(TaskCallbackParser.TryParse($"task:done-step:{taskId}:1", out TaskCallback doneStepTaskCallback), "TaskCallbackParser parses done-step callback");
    AssertEqual(TaskCallbackVerb.DoneStep, doneStepTaskCallback.Verb, "TaskCallbackParser reads done-step verb");
    AssertEqual(taskId, doneStepTaskCallback.TaskId, "TaskCallbackParser reads done-step task id");
    AssertEqual(1, doneStepTaskCallback.StepNumber, "TaskCallbackParser reads done-step step number");
    AssertTrue(TaskCallbackParser.TryParse($"task:cancel:{taskId}", out TaskCallback cancelTaskCallback), "TaskCallbackParser parses cancel callback");
    AssertEqual(TaskCallbackVerb.Cancel, cancelTaskCallback.Verb, "TaskCallbackParser reads cancel verb");
    AssertFalse(TaskCallbackParser.TryParse("act:details:1", out _), "TaskCallbackParser rejects pending-action domain");
    AssertFalse(TaskCallbackParser.TryParse("task:unknown:1", out _), "TaskCallbackParser rejects unknown task verb");
    AssertFalse(TaskCallbackParser.TryParse("task:open:nope", out _), "TaskCallbackParser rejects non-numeric task id");
    AssertFalse(TaskCallbackParser.TryParse("task:open", out _), "TaskCallbackParser rejects missing task id");
    AssertFalse(TaskCallbackParser.TryParse("task:open:1:extra", out _), "TaskCallbackParser rejects extra callback parts");
    AssertFalse(TaskCallbackParser.TryParse("task:done-step:1", out _), "TaskCallbackParser rejects done-step without step number");
    AssertFalse(TaskCallbackParser.TryParse("task:done-step:1:nope", out _), "TaskCallbackParser rejects non-numeric done-step step number");

    InlineKeyboardMarkup taskDetailsMarkup = InlineKeyboardFactory.ForTaskDetails(taskId);
    AssertTrue(taskDetailsMarkup.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Done" && button.CallbackData == $"task:done:{taskId}"), "InlineKeyboardFactory creates task done button");
    AssertTrue(taskDetailsMarkup.InlineKeyboard.SelectMany(row => row).Any(button => button.Text == "Cancel" && button.CallbackData == $"task:cancel:{taskId}"), "InlineKeyboardFactory creates task cancel button");

    CommandResult taskResult = await commandRouter.TryHandleAsync(TextMessage($"/task {taskId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(taskResult.Handled, "/task is handled");
    AssertTrue(taskResult.ReplyText?.Contains("[ ] 1.") == true, "/task shows task steps");
    AssertTrue(taskResult.ReplyMarkup is not null, "/task includes inline task action keyboard metadata");
    AssertTrue(taskResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.CallbackData == $"task:done:{taskId}"), "/task keyboard includes done callback");
    AssertTrue(taskResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.CallbackData == $"task:done-step:{taskId}:1"), "/task keyboard includes done-step callback for step 1");
    AssertTrue(taskResult.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Any(button => button.CallbackData == $"task:cancel:{taskId}"), "/task keyboard includes cancel callback");

    var taskCallbackService = new TaskCallbackService(agentTaskService, adminTestSettings);
    TaskCallbackResult invalidTaskCallbackResult = await taskCallbackService.HandleAsync("act:details:1", testUser, testUser.ChatId, dbContext, CancellationToken.None);
    AssertFalse(invalidTaskCallbackResult.Handled, "TaskCallbackService ignores non-task callback domains");
    TaskCallbackResult openTaskCallbackResult = await taskCallbackService.HandleAsync($"task:open:{taskId}", testUser, testUser.ChatId, dbContext, CancellationToken.None);
    AssertTrue(openTaskCallbackResult.Handled, "TaskCallbackService handles open callbacks");
    AssertTrue(openTaskCallbackResult.AnswerText.Contains("Opened"), "TaskCallbackService answers open callbacks");
    AssertTrue(openTaskCallbackResult.MessageText?.Contains($"Task #{taskId}") == true, "TaskCallbackService open returns task details");
    AssertTrue(openTaskCallbackResult.MessageText?.Contains("inventory API", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService open includes task goal");
    TaskCallbackResult missingStepTaskCallbackResult = await taskCallbackService.HandleAsync($"task:done-step:{taskId}:99", testUser, testUser.ChatId, dbContext, CancellationToken.None);
    AssertTrue(missingStepTaskCallbackResult.Handled, "TaskCallbackService handles missing done-step callbacks safely");
    AssertTrue(missingStepTaskCallbackResult.MessageText?.Contains("does not have step 99", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService reports missing step");
    AssertFalse((await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 1, CancellationToken.None)).IsDone, "TaskCallbackService missing done-step callback does not mutate other steps");
    TaskCallbackResult doneStepTaskCallbackResult = await taskCallbackService.HandleAsync($"task:done-step:{taskId}:1", testUser, testUser.ChatId, dbContext, CancellationToken.None);
    AssertTrue(doneStepTaskCallbackResult.Handled, "TaskCallbackService handles done-step callbacks safely");
    AssertTrue(doneStepTaskCallbackResult.AnswerText.Contains("Done", StringComparison.OrdinalIgnoreCase), "TaskCallbackService answers successful done-step callbacks");
    AssertTrue(doneStepTaskCallbackResult.MessageText?.Contains("step 1 marked done", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService reports done-step success");
    AssertTrue(doneStepTaskCallbackResult.MessageText?.Contains("[x] 1.", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService returns updated task details after done-step");
    AssertTrue((await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 1, CancellationToken.None)).IsDone, "TaskCallbackService done-step callback marks the selected step done");
    AssertFalse((await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 2, CancellationToken.None)).IsDone, "TaskCallbackService done-step callback does not mark other steps done");
    AgentTask cancelCallbackTask = await agentTaskService.CreatePlanAsync(dbContext, testUser, "cancel from inline button", CancellationToken.None);
    TaskCallbackResult cancelTaskCallbackResult = await taskCallbackService.HandleAsync($"task:cancel:{cancelCallbackTask.Id}", testUser, testUser.ChatId, dbContext, CancellationToken.None);
    AssertTrue(cancelTaskCallbackResult.Handled, "TaskCallbackService handles cancel callbacks safely");
    AssertTrue(cancelTaskCallbackResult.AnswerText.Contains("Cancelled", StringComparison.OrdinalIgnoreCase), "TaskCallbackService answers successful cancel callbacks");
    AssertTrue(cancelTaskCallbackResult.MessageText?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService reports cancel success");
    AssertEqual(AgentTaskStatuses.Cancelled, (await dbContext.AgentTasks.FindAsync([cancelCallbackTask.Id], CancellationToken.None))!.Status, "TaskCallbackService cancel callback cancels the selected task");
    AssertEqual(AgentTaskStatuses.Active, (await dbContext.AgentTasks.FindAsync([taskId], CancellationToken.None))!.Status, "TaskCallbackService cancel callback does not cancel a different task");

    AgentTask wholeDoneTask = await agentTaskService.CreatePlanAsync(dbContext, testUser, "ship the whole done button", CancellationToken.None);
    TaskCallbackResult doneTaskCallbackResult = await taskCallbackService.HandleAsync($"task:done:{wholeDoneTask.Id}", testUser, testUser.ChatId, dbContext, CancellationToken.None);
    AssertTrue(doneTaskCallbackResult.Handled, "TaskCallbackService handles done callbacks safely");
    AssertTrue(doneTaskCallbackResult.AnswerText.Contains("Done", StringComparison.OrdinalIgnoreCase), "TaskCallbackService answers successful done callbacks");
    AssertTrue(doneTaskCallbackResult.MessageText?.Contains("marked completed", StringComparison.OrdinalIgnoreCase) == true, "TaskCallbackService reports whole-task done success");
    AssertEqual(AgentTaskStatuses.Completed, (await dbContext.AgentTasks.FindAsync([wholeDoneTask.Id], CancellationToken.None))!.Status, "TaskCallbackService done callback completes the selected task");
    AssertEqual(5, await dbContext.AgentTaskSteps.CountAsync(x => x.AgentTaskId == wholeDoneTask.Id && x.IsDone, CancellationToken.None), "TaskCallbackService done callback marks all selected task steps done");
    AssertEqual(AgentTaskStatuses.Active, (await dbContext.AgentTasks.FindAsync([taskId], CancellationToken.None))!.Status, "TaskCallbackService done callback does not complete a different task");

    CommandResult invalidScheduleResult = await commandRouter.TryHandleAsync(TextMessage($"/schedule {taskId} 1 yesterday 09:00"), testUser, dbContext, CancellationToken.None);
    AssertTrue(invalidScheduleResult.Handled, "/schedule invalid time is handled");
    AssertTrue(invalidScheduleResult.ReplyText?.Contains("Usage: /schedule <task-id> <step-number> <time> [note]") == true, "/schedule invalid time explains usage");

    CommandResult scheduleResult = await commandRouter.TryHandleAsync(TextMessage($"/schedule {taskId} 1 in 30m Review the first step"), testUser, dbContext, CancellationToken.None);
    AssertTrue(scheduleResult.Handled, "/schedule is handled");
    AssertTrue(scheduleResult.ReplyText?.Contains("scheduled", StringComparison.OrdinalIgnoreCase) == true, "/schedule reports scheduled result");
    AgentTaskStep scheduledCommandStep = await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 1, CancellationToken.None);
    AssertTrue(scheduledCommandStep.ScheduledAtUtc.HasValue, "/schedule stores scheduled time");
    AssertTrue(scheduledCommandStep.ScheduledAtUtc > DateTime.UtcNow, "/schedule stores future scheduled time");
    AssertEqual("Review the first step", scheduledCommandStep.ScheduleNote, "/schedule stores note after schedule expression");

    CommandResult scheduleListResult = await commandRouter.TryHandleAsync(TextMessage("/schedulelist"), testUser, dbContext, CancellationToken.None);
    AssertTrue(scheduleListResult.Handled, "/schedulelist is handled");
    AssertTrue(scheduleListResult.ReplyText?.Contains($"Task #{taskId}") == true, "/schedulelist shows task id");
    AssertTrue(scheduleListResult.ReplyText?.Contains("Review the first step") == true, "/schedulelist shows schedule note");

    CommandResult unscheduleResult = await commandRouter.TryHandleAsync(TextMessage($"/unschedule {taskId} 1"), testUser, dbContext, CancellationToken.None);
    AssertTrue(unscheduleResult.Handled, "/unschedule is handled");
    AssertTrue(unscheduleResult.ReplyText?.Contains("unscheduled", StringComparison.OrdinalIgnoreCase) == true, "/unschedule reports cleared schedule");
    AgentTaskStep unscheduledCommandStep = await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 1, CancellationToken.None);
    AssertFalse(unscheduledCommandStep.ScheduledAtUtc.HasValue, "/unschedule clears scheduled time");
    AssertFalse(unscheduledCommandStep.ReminderSentAtUtc.HasValue, "/unschedule clears reminder sent time");
    AssertTrue(string.IsNullOrWhiteSpace(unscheduledCommandStep.ScheduleNote), "/unschedule clears schedule note");

    AssertFalse((await commandRouter.TryHandleAsync(TextMessage("/schedulex 1 1 in 30m"), testUser, dbContext, CancellationToken.None)).Handled, "/schedulex is not treated as /schedule");

    CommandResult doneStepResult = await commandRouter.TryHandleAsync(TextMessage($"/done {taskId} 1"), testUser, dbContext, CancellationToken.None);
    AssertTrue(doneStepResult.Handled, "/done step is handled");
    AssertEqual("👍", doneStepResult.ReactionEmoji, "/done requests thumbs-up reaction metadata");
    AssertTrue((await dbContext.AgentTaskSteps.FirstAsync(x => x.AgentTaskId == taskId && x.StepNumber == 1)).IsDone, "/done marks step done");

    CommandResult cancelTaskResult = await commandRouter.TryHandleAsync(TextMessage($"/cancel {taskId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(cancelTaskResult.Handled, "/cancel is handled");
    AssertEqual(AgentTaskStatuses.Cancelled, (await dbContext.AgentTasks.FindAsync([taskId], CancellationToken.None))!.Status, "/cancel marks task cancelled");

    await DocumentTests.RunDocumentMediaCommandTestsAsync(
        commandRouter,
        testUser,
        nonAdminUser,
        dbContext,
        documentStorage,
        documentQaChatClient,
        documentSummaryChatClient,
        adminTestSettings,
        importDirectory);

    dbContext.Messages.Add(new ChatMessage
    {
        ConnectedUserId = testUser.Id,
        ChatId = testUser.ChatId,
        Content = "hello",
        Role = ChatRoles.User,
        Timestamp = DateTime.UtcNow
    });
    await dbContext.SaveChangesAsync();

    CommandResult resetResult = await commandRouter.TryHandleAsync(TextMessage("/reset"), testUser, dbContext, CancellationToken.None);
    AssertTrue(resetResult.Handled, "/reset is handled");
    AssertEqual("🧹", resetResult.ReactionEmoji, "/reset requests broom reaction metadata");
    AssertEqual(0, await dbContext.Messages.CountAsync(x => x.ConnectedUserId == testUser.Id), "/reset deletes user messages");

    CommandResult rememberResult = await commandRouter.TryHandleAsync(TextMessage("/remember User prefers concise answers"), testUser, dbContext, CancellationToken.None);
    AssertTrue(rememberResult.Handled, "/remember is handled");
    AssertEqual("✅", rememberResult.ReactionEmoji, "/remember requests success reaction metadata");
    AssertEqual(1, await dbContext.Memories.CountAsync(x => x.ConnectedUserId == testUser.Id), "/remember saves memory");

    CommandResult mentionRememberResult = await commandRouter.TryHandleAsync(TextMessage("/remember@red_eye_ghost_bot User likes exact commands"), testUser, dbContext, CancellationToken.None);
    AssertTrue(mentionRememberResult.Handled, "/remember@bot is handled");
    AssertEqual(2, await dbContext.Memories.CountAsync(x => x.ConnectedUserId == testUser.Id), "/remember@bot saves memory using parsed arguments");

    CommandResult rememberPrefixResult = await commandRouter.TryHandleAsync(TextMessage("/remembered wrong command"), testUser, dbContext, CancellationToken.None);
    AssertFalse(rememberPrefixResult.Handled, "/remembered is not treated as /remember");

    CommandResult memoryResult = await commandRouter.TryHandleAsync(TextMessage("/memory"), testUser, dbContext, CancellationToken.None);
    AssertTrue(memoryResult.Handled, "/memory is handled");
    AssertTrue(memoryResult.ReplyText?.Contains("User prefers concise answers") == true, "/memory lists saved memory");

    int memoryId = await dbContext.Memories
        .Where(x => x.ConnectedUserId == testUser.Id)
        .Select(x => x.Id)
        .FirstAsync();

    CommandResult forgetResult = await commandRouter.TryHandleAsync(TextMessage($"/forget {memoryId}"), testUser, dbContext, CancellationToken.None);
    AssertTrue(forgetResult.Handled, "/forget is handled");
    AssertEqual(1, await dbContext.Memories.CountAsync(x => x.ConnectedUserId == testUser.Id), "/forget deletes one memory");
}

await using (var cleanupContext = new TelegramDbContext())
{
    await cleanupContext.Database.EnsureDeletedAsync();
}
Environment.SetEnvironmentVariable("TELEGRAM_DB_CONNECTION", previousConnection);

Console.WriteLine("All TelegramMessagingTool helper tests passed.");

sealed class DeterministicEmbeddingService : ITextEmbeddingService
{
    public Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        string normalized = text.ToLowerInvariant();
        if (normalized.Contains("saved") || normalized.Contains("note"))
        {
            return Task.FromResult<IReadOnlyList<float>>([0.0f, 1.0f]);
        }

        return Task.FromResult<IReadOnlyList<float>>([1.0f, 0.0f]);
    }
}

sealed record ProcessRunResult(int ExitCode, string Output, string Error);

static class TestDirectoryCleanup
{
    public static void DeleteRecursive(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}

static class TestProcessRunner
{
    public static async Task<ProcessRunResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(processStartInfo) ?? throw new InvalidOperationException($"Failed to start {executable}.");
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process {executable} failed with exit code {process.ExitCode}.\nSTDOUT:\n{output}\nSTDERR:\n{error}");
        }

        return new ProcessRunResult(process.ExitCode, output, error);
    }
}

sealed class FakeProcessTerminator : IProcessTerminator
{
    public int? LastRequestedPid { get; private set; }

    public int KillCallCount { get; private set; }

    public ProcessTerminationResult Terminate(int processId)
    {
        LastRequestedPid = processId;
        KillCallCount++;
        return ProcessTerminationResult.Ok($"Process PID {processId} was terminated successfully.");
    }
}

sealed class FakeLatestReleaseRestarter : ILatestReleaseRestarter
{
    public string? LastProjectRoot { get; private set; }

    public int RestartCallCount { get; private set; }

    public Task<LatestReleaseRestartResult> RestartAsync(RestartLatestBotPayload payload, CancellationToken cancellationToken)
    {
        LastProjectRoot = payload.ProjectRoot;
        RestartCallCount++;
        return Task.FromResult(LatestReleaseRestartResult.Ok($"Scheduled restart from latest release under {payload.ProjectRoot}."));
    }
}

sealed class FakeGitHubIssueCreator : IGitHubIssueCreator
{
    public int CreateCallCount { get; private set; }

    public GitHubCreateIssuePayload? LastPayload { get; private set; }

    public Task<GitHubIssueCreateResult> CreateIssueAsync(GitHubCreateIssuePayload payload, CancellationToken cancellationToken)
    {
        CreateCallCount++;
        LastPayload = payload;
        return Task.FromResult(GitHubIssueCreateResult.Ok(
            777,
            "https://github.com/mujahedgt/TelegramMessagingTool/issues/777",
            "Created GitHub issue #777: https://github.com/mujahedgt/TelegramMessagingTool/issues/777"));
    }
}

sealed class FakeGitHubIssueCommenter : IGitHubIssueCommenter
{
    public int CommentCallCount { get; private set; }

    public GitHubCommentIssuePayload? LastPayload { get; private set; }

    public Task<GitHubIssueCommentResult> CommentAsync(GitHubCommentIssuePayload payload, CancellationToken cancellationToken)
    {
        CommentCallCount++;
        LastPayload = payload;
        return Task.FromResult(GitHubIssueCommentResult.Ok(
            "https://github.com/mujahedgt/TelegramMessagingTool/issues/777#issuecomment-123",
            "Created GitHub issue comment on #777: https://github.com/mujahedgt/TelegramMessagingTool/issues/777#issuecomment-123"));
    }
}

sealed class FakeTaskReminderSender : ITaskReminderSender
{
    public List<(long ChatId, string Text)> SentMessages { get; } = [];

    public Task SendReminderAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        SentMessages.Add((chatId, text));
        return Task.CompletedTask;
    }
}

sealed class FakeHighRiskTool : IAgentTool
{
    public string Name => "fake_high_risk";

    public string Description => "Fake approval-backed test tool.";

    public bool RequiresApproval => true;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Fail("approval required"));
    }
}

sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseBody;

    public FakeHttpMessageHandler(string responseBody)
    {
        _responseBody = responseBody;
    }

    public string? LastRequestAuthorization { get; private set; }

    public Uri? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestAuthorization = request.Headers.Authorization?.ToString();
        LastRequestUri = request.RequestUri;
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(_responseBody)
        };
        return Task.FromResult(response);
    }
}

sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<string> _responses;

    public ScriptedChatClient(IEnumerable<string> responses)
    {
        _responses = new Queue<string>(responses);
    }

    public int Calls { get; private set; }

    public List<ModelTaskKind> ModelTaskKinds { get; } = [];

    public Task<string> AskAsync(
        List<OllamaMessageDto> conversationContext,
        CancellationToken cancellationToken,
        ModelTaskKind taskKind = ModelTaskKind.Chat)
    {
        Calls++;
        ModelTaskKinds.Add(taskKind);
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : "No scripted response left.");
    }
}

sealed class FakeSearchTool : IAgentTool
{
    public string Name => "online_search";

    public string Description => "Fake search tool for tests.";

    public bool RequiresApproval => false;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Ok($"Result for {input}\nhttps://example.com/source"));
    }
}
