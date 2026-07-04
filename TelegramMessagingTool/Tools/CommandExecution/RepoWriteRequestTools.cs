using System.Diagnostics;
using System.Text.Json;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Tools.CommandExecution;

public sealed class RepoReplaceTextRequestTool : IApprovalRequestTool
{
    private const int MaxTextLength = 1_500;

    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;
    private readonly string _projectRoot;

    public RepoReplaceTextRequestTool(PendingActionService pendingActionService, BotSettings settings, string projectRoot)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    public string Name => "repo_replace_text";

    public string Description => "Approval-gated repository edit: replace one exact text block inside a source/docs file under SAFE_COMMAND_PROJECT_ROOT. Strict JSON only: {\"path\":\"relative/file.cs\",\"old_text\":\"existing text\",\"new_text\":\"replacement text\",\"reason\":\"why\"}.";

    public bool RequiresApproval => true;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Fail(
            "repo_replace_text requires an authenticated pending-action context. Use it from the Telegram/console agent flow so it can create an approval request."));
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

        if (!Directory.Exists(_projectRoot))
        {
            return ToolResult.Fail($"Project root does not exist: {_projectRoot}");
        }

        if (!TryParseInput(input, _projectRoot, out RepoReplaceTextPayload payload, out string error))
        {
            return ToolResult.Fail(error);
        }

        string payloadJson = JsonSerializer.Serialize(payload);
        if (payloadJson.Length > 3_800)
        {
            return ToolResult.Fail("repo_replace_text payload is too large for the pending-action store. Use a smaller exact replacement.");
        }

        PendingAction pendingAction = await _pendingActionService.CreateAsync(
            dbContext,
            user,
            toolName: Name,
            description: $"Replace text in {payload.Path}. Reason: {payload.Reason}",
            payloadJson: payloadJson,
            riskLevel: "high",
            ttl: TimeSpan.FromMinutes(15),
            cancellationToken: cancellationToken);

        return ToolResult.Ok($"""
Approval required.

Pending action #{pendingAction.Id}
Type: repo_replace_text
Risk: high
File: {payload.Path}
Reason: {payload.Reason}
Expires UTC: {pendingAction.ExpiresAt:yyyy-MM-dd HH:mm}

This request only created a pending action. It did not edit files, run tests, or commit changes yet.
Approve: /approve {pendingAction.Id}
Deny: /deny {pendingAction.Id}
""");
    }

    public static bool TryParseInput(string input, string projectRoot, out RepoReplaceTextPayload payload, out string error)
    {
        payload = RepoReplaceTextPayload.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "repo_replace_text input must be strict JSON with path, old_text, and new_text.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "repo_replace_text input must be a JSON object.";
                return false;
            }

            string path = ReadString(root, "path").Trim();
            string oldText = ReadString(root, "old_text");
            string newText = ReadString(root, "new_text");
            string reason = ReadString(root, "reason").Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "requested by agent";
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "repo_replace_text requires a non-empty relative path.";
                return false;
            }

            if (string.IsNullOrEmpty(oldText))
            {
                error = "repo_replace_text requires non-empty old_text so the edit can be verified exactly.";
                return false;
            }

            if (oldText.Length > MaxTextLength || newText.Length > MaxTextLength)
            {
                error = $"repo_replace_text old_text and new_text must each be {MaxTextLength} characters or less.";
                return false;
            }

            if (reason.Length > 300)
            {
                reason = reason[..300];
            }

            if (!RepoWritePathPolicy.TryResolveProjectFile(projectRoot, path, out string fullPath, out error))
            {
                return false;
            }

            payload = new RepoReplaceTextPayload(
                Action: "repo_replace_text",
                ProjectRoot: Path.GetFullPath(projectRoot),
                Path: path.Replace('\\', '/'),
                FullPath: fullPath,
                OldText: oldText,
                NewText: newText,
                Reason: reason,
                RequestedAtUtc: DateTime.UtcNow);
            return true;
        }
        catch (JsonException)
        {
            error = "repo_replace_text input must be valid JSON, for example {\"path\":\"Program.cs\",\"old_text\":\"old\",\"new_text\":\"new\"}.";
            return false;
        }
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;
    }
}

public sealed class RepoCommitChangesRequestTool : IApprovalRequestTool
{
    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;
    private readonly string _projectRoot;

    public RepoCommitChangesRequestTool(PendingActionService pendingActionService, BotSettings settings, string projectRoot)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    public string Name => "repo_commit_changes";

    public string Description => "Approval-gated repository commit: runs safe Git checks and commits current allowed project changes under SAFE_COMMAND_PROJECT_ROOT. Strict JSON only: {\"message\":\"commit subject\",\"body\":\"optional body\"}. Does not push.";

    public bool RequiresApproval => true;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Fail(
            "repo_commit_changes requires an authenticated pending-action context. Use it from the Telegram/console agent flow so it can create an approval request."));
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

        if (!Directory.Exists(_projectRoot))
        {
            return ToolResult.Fail($"Project root does not exist: {_projectRoot}");
        }

        if (!TryParseInput(input, _projectRoot, out RepoCommitChangesPayload payload, out string error))
        {
            return ToolResult.Fail(error);
        }

        PendingAction pendingAction = await _pendingActionService.CreateAsync(
            dbContext,
            user,
            toolName: Name,
            description: $"Commit approved repository changes. Message: {payload.Message}",
            payloadJson: JsonSerializer.Serialize(payload),
            riskLevel: "high",
            ttl: TimeSpan.FromMinutes(15),
            cancellationToken: cancellationToken);

        return ToolResult.Ok($"""
Approval required.

Pending action #{pendingAction.Id}
Type: repo_commit_changes
Risk: high
Project root: {payload.ProjectRoot}
Commit message: {payload.Message}
Expires UTC: {pendingAction.ExpiresAt:yyyy-MM-dd HH:mm}

This request only created a pending action. It did not commit or push changes yet.
Approve: /approve {pendingAction.Id}
Deny: /deny {pendingAction.Id}
""");
    }

    public static bool TryParseInput(string input, string projectRoot, out RepoCommitChangesPayload payload, out string error)
    {
        payload = RepoCommitChangesPayload.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "repo_commit_changes input must be strict JSON with a non-empty message.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "repo_commit_changes input must be a JSON object.";
                return false;
            }

            string message = ReadString(root, "message").Trim();
            string body = ReadString(root, "body").Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                error = "repo_commit_changes requires a non-empty commit message.";
                return false;
            }

            if (message.Contains('\n') || message.Contains('\r'))
            {
                error = "repo_commit_changes commit message must be a single-line subject. Put details in body.";
                return false;
            }

            if (message.Length > 120)
            {
                error = "repo_commit_changes commit message must be 120 characters or less.";
                return false;
            }

            if (body.Length > 1_000)
            {
                body = body[..1_000];
            }

            payload = new RepoCommitChangesPayload(
                Action: "repo_commit_changes",
                ProjectRoot: Path.GetFullPath(projectRoot),
                Message: message,
                Body: body,
                RequestedAtUtc: DateTime.UtcNow);
            return true;
        }
        catch (JsonException)
        {
            error = "repo_commit_changes input must be valid JSON, for example {\"message\":\"Update help text\",\"body\":\"Optional details\"}.";
            return false;
        }
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;
    }
}

public sealed class RepoPushChangesRequestTool : IApprovalRequestTool
{
    private readonly PendingActionService _pendingActionService;
    private readonly BotSettings _settings;
    private readonly string _projectRoot;

    public RepoPushChangesRequestTool(PendingActionService pendingActionService, BotSettings settings, string projectRoot)
    {
        _pendingActionService = pendingActionService;
        _settings = settings;
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    public string Name => "repo_push_changes";

    public string Description => "Approval-gated repository push: refuses dirty working trees and pushes the current branch to origin with fixed git arguments. Strict JSON only: {\"reason\":\"why push is needed\"}. No force push.";

    public bool RequiresApproval => true;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Fail(
            "repo_push_changes requires an authenticated pending-action context. Use it from the Telegram/console agent flow so it can create an approval request."));
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

        if (!Directory.Exists(_projectRoot))
        {
            return ToolResult.Fail($"Project root does not exist: {_projectRoot}");
        }

        if (!TryParseInput(input, _projectRoot, out RepoPushChangesPayload payload, out string error))
        {
            return ToolResult.Fail(error);
        }

        PendingAction pendingAction = await _pendingActionService.CreateAsync(
            dbContext,
            user,
            toolName: Name,
            description: $"Push current branch to origin. Reason: {payload.Reason}",
            payloadJson: JsonSerializer.Serialize(payload),
            riskLevel: "high",
            ttl: TimeSpan.FromMinutes(15),
            cancellationToken: cancellationToken);

        return ToolResult.Ok($"""
Approval required.

Pending action #{pendingAction.Id}
Type: repo_push_changes
Risk: high
Project root: {payload.ProjectRoot}
Reason: {payload.Reason}
Expires UTC: {pendingAction.ExpiresAt:yyyy-MM-dd HH:mm}

This request only created a pending action. It did not push changes yet.
Approve: /approve {pendingAction.Id}
Deny: /deny {pendingAction.Id}
""");
    }

    public static bool TryParseInput(string input, string projectRoot, out RepoPushChangesPayload payload, out string error)
    {
        payload = RepoPushChangesPayload.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "repo_push_changes input must be strict JSON with an optional reason.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "repo_push_changes input must be a JSON object.";
                return false;
            }

            string reason = ReadString(root, "reason").Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "requested by agent";
            }

            if (reason.Length > 300)
            {
                reason = reason[..300];
            }

            payload = new RepoPushChangesPayload(
                Action: "repo_push_changes",
                ProjectRoot: Path.GetFullPath(projectRoot),
                Reason: reason,
                RequestedAtUtc: DateTime.UtcNow);
            return true;
        }
        catch (JsonException)
        {
            error = "repo_push_changes input must be valid JSON, for example {\"reason\":\"Push approved local commit\"}.";
            return false;
        }
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;
    }
}

public static class RepoGitPushExecutor
{
    private const int TimeoutMilliseconds = 120_000;

    public static async Task<RepoGitPushResult> PushAsync(RepoPushChangesPayload payload, CancellationToken cancellationToken)
    {
        string projectRoot = Path.GetFullPath(payload.ProjectRoot);
        if (!Directory.Exists(projectRoot))
        {
            return RepoGitPushResult.Fail($"Execution failed: project root does not exist: {projectRoot}");
        }

        ProcessCommandResult gitRoot = await RunGitAsync(projectRoot, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (!gitRoot.Success)
        {
            return RepoGitPushResult.Fail("Execution failed: project root is not a git repository. " + gitRoot.RenderForMessage());
        }

        string repoRoot = Path.GetFullPath(gitRoot.Output.Trim());
        if (!string.Equals(repoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return RepoGitPushResult.Fail("Execution refused: SAFE_COMMAND_PROJECT_ROOT must be the git repository root for repo_push_changes.");
        }

        ProcessCommandResult status = await RunGitAsync(projectRoot, ["status", "--porcelain"], cancellationToken);
        if (!status.Success)
        {
            return RepoGitPushResult.Fail("Execution failed: could not read git status. " + status.RenderForMessage());
        }

        if (!string.IsNullOrWhiteSpace(status.Output))
        {
            return RepoGitPushResult.Fail("Execution refused: working tree has uncommitted changes. Commit or discard changes before pushing.");
        }

        ProcessCommandResult branch = await RunGitAsync(projectRoot, ["branch", "--show-current"], cancellationToken);
        if (!branch.Success)
        {
            return RepoGitPushResult.Fail("Execution failed: could not detect current branch. " + branch.RenderForMessage());
        }

        string branchName = branch.Output.Trim();
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return RepoGitPushResult.Fail("Execution refused: repository is in detached HEAD state; repo_push_changes only pushes a named current branch.");
        }

        ProcessCommandResult push = await RunGitAsync(projectRoot, ["push", "origin", branchName], cancellationToken);
        if (!push.Success)
        {
            return RepoGitPushResult.Fail("Execution failed: git push origin <current-branch> failed. " + push.RenderForMessage());
        }

        return RepoGitPushResult.Ok($"Pushed current branch '{branchName}' to origin. {push.RenderForMessage()}");
    }

    private static async Task<ProcessCommandResult> RunGitAsync(string projectRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GCM_INTERACTIVE"] = "Never";

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeoutMilliseconds);
            string output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            string error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return new ProcessCommandResult(process.ExitCode, output, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProcessCommandResult(-1, string.Empty, "git command timed out.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ProcessCommandResult(-1, string.Empty, ex.Message);
        }
    }
}

public static class RepoGitCommitExecutor
{
    private const int TimeoutMilliseconds = 120_000;

    public static async Task<RepoGitCommitResult> CommitAsync(RepoCommitChangesPayload payload, CancellationToken cancellationToken)
    {
        string projectRoot = Path.GetFullPath(payload.ProjectRoot);
        if (!Directory.Exists(projectRoot))
        {
            return RepoGitCommitResult.Fail($"Execution failed: project root does not exist: {projectRoot}");
        }

        ProcessCommandResult gitRoot = await RunGitAsync(projectRoot, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (!gitRoot.Success)
        {
            return RepoGitCommitResult.Fail("Execution failed: project root is not a git repository. " + gitRoot.RenderForMessage());
        }

        string repoRoot = Path.GetFullPath(gitRoot.Output.Trim());
        if (!string.Equals(repoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return RepoGitCommitResult.Fail("Execution refused: SAFE_COMMAND_PROJECT_ROOT must be the git repository root for repo_commit_changes.");
        }

        ProcessCommandResult status = await RunGitAsync(projectRoot, ["status", "--porcelain"], cancellationToken);
        if (!status.Success)
        {
            return RepoGitCommitResult.Fail("Execution failed: could not read git status. " + status.RenderForMessage());
        }

        IReadOnlyList<string> changedPaths = ParsePorcelainPaths(status.Output);
        if (changedPaths.Count == 0)
        {
            return RepoGitCommitResult.Fail("Execution refused: there are no repository changes to commit.");
        }

        foreach (string changedPath in changedPaths)
        {
            if (!RepoWritePathPolicy.TryResolveProjectFile(projectRoot, changedPath, out _, out string error))
            {
                return RepoGitCommitResult.Fail($"Execution refused: changed path '{changedPath}' is not allowed. {error}");
            }
        }

        ProcessCommandResult diffCheck = await RunGitAsync(projectRoot, ["diff", "--check"], cancellationToken);
        if (!diffCheck.Success)
        {
            return RepoGitCommitResult.Fail("Execution refused: git diff --check failed. " + diffCheck.RenderForMessage());
        }

        var addArgs = new List<string> { "add", "--" };
        addArgs.AddRange(changedPaths);
        ProcessCommandResult add = await RunGitAsync(projectRoot, addArgs, cancellationToken);
        if (!add.Success)
        {
            return RepoGitCommitResult.Fail("Execution failed: git add failed. " + add.RenderForMessage());
        }

        var commitArgs = new List<string> { "commit", "-m", payload.Message };
        if (!string.IsNullOrWhiteSpace(payload.Body))
        {
            commitArgs.Add("-m");
            commitArgs.Add(payload.Body);
        }

        ProcessCommandResult commit = await RunGitAsync(projectRoot, commitArgs, cancellationToken);
        if (!commit.Success)
        {
            return RepoGitCommitResult.Fail("Execution failed: git commit failed. " + commit.RenderForMessage());
        }

        ProcessCommandResult lastCommit = await RunGitAsync(projectRoot, ["rev-parse", "--short", "HEAD"], cancellationToken);
        string commitId = lastCommit.Success ? lastCommit.Output.Trim() : "unknown";
        return RepoGitCommitResult.Ok($"Committed {changedPaths.Count} file(s) as {commitId}: {payload.Message}");
    }

    private static IReadOnlyList<string> ParsePorcelainPaths(string porcelain)
    {
        var paths = new List<string>();
        foreach (string rawLine in porcelain.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 4)
            {
                continue;
            }

            string pathPart = rawLine[3..].Trim();
            int renameArrow = pathPart.IndexOf(" -> ", StringComparison.Ordinal);
            if (renameArrow >= 0)
            {
                pathPart = pathPart[(renameArrow + 4)..].Trim();
            }

            pathPart = pathPart.Trim('"').Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(pathPart))
            {
                paths.Add(pathPart);
            }
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<ProcessCommandResult> RunGitAsync(string projectRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeoutMilliseconds);
            string output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            string error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return new ProcessCommandResult(process.ExitCode, output, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProcessCommandResult(-1, string.Empty, "git command timed out.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ProcessCommandResult(-1, string.Empty, ex.Message);
        }
    }
}

public static class RepoWritePathPolicy
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".slnx",
        ".json",
        ".md",
        ".txt",
        ".yml",
        ".yaml"
    };

    private static readonly string[] BlockedSegments = [
        ".git",
        "bin",
        "obj",
        "release",
        "UserFiles",
        "ImportInbox"
    ];

    public static bool TryResolveProjectFile(string projectRoot, string relativePath, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;

        string normalizedRoot = Path.GetFullPath(projectRoot);
        if (Path.IsPathRooted(relativePath))
        {
            error = "repo write path must be relative to SAFE_COMMAND_PROJECT_ROOT, not absolute.";
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        string rootWithSeparator = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            error = "repo write path must stay inside SAFE_COMMAND_PROJECT_ROOT.";
            return false;
        }

        string extension = Path.GetExtension(candidate);
        if (!AllowedExtensions.Contains(extension))
        {
            error = $"repo write only supports source/docs/config text files ({string.Join(", ", AllowedExtensions.Order())}).";
            return false;
        }

        string[] segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(segment => BlockedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
        {
            error = "repo write refuses edits under generated, runtime, release, import, user-file, or git metadata folders.";
            return false;
        }

        fullPath = candidate;
        return true;
    }
}

public sealed record RepoReplaceTextPayload(
    string Action,
    string ProjectRoot,
    string Path,
    string FullPath,
    string OldText,
    string NewText,
    string Reason,
    DateTime RequestedAtUtc)
{
    public static RepoReplaceTextPayload Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        DateTime.MinValue);
}

public sealed record RepoCommitChangesPayload(
    string Action,
    string ProjectRoot,
    string Message,
    string Body,
    DateTime RequestedAtUtc)
{
    public static RepoCommitChangesPayload Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        DateTime.MinValue);
}

public sealed record RepoPushChangesPayload(
    string Action,
    string ProjectRoot,
    string Reason,
    DateTime RequestedAtUtc)
{
    public static RepoPushChangesPayload Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        DateTime.MinValue);
}

public sealed record RepoGitCommitResult(bool Success, string Message)
{
    public static RepoGitCommitResult Ok(string message) => new(true, message);

    public static RepoGitCommitResult Fail(string message) => new(false, message);
}

public sealed record RepoGitPushResult(bool Success, string Message)
{
    public static RepoGitPushResult Ok(string message) => new(true, message);

    public static RepoGitPushResult Fail(string message) => new(false, message);
}

public sealed record ProcessCommandResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;

    public string RenderForMessage()
    {
        string combined = $"exit={ExitCode}\nstdout:\n{Output}\nstderr:\n{Error}";
        return combined.Length <= 2_000 ? combined : combined[..2_000] + "\n... truncated ...";
    }
}
