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
            error = "repo_replace_text path must be relative to SAFE_COMMAND_PROJECT_ROOT, not absolute.";
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        string rootWithSeparator = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            error = "repo_replace_text path must stay inside SAFE_COMMAND_PROJECT_ROOT.";
            return false;
        }

        string extension = Path.GetExtension(candidate);
        if (!AllowedExtensions.Contains(extension))
        {
            error = $"repo_replace_text only supports source/docs/config text files ({string.Join(", ", AllowedExtensions.Order())}).";
            return false;
        }

        string[] segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(segment => BlockedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
        {
            error = "repo_replace_text refuses edits under generated, runtime, release, import, user-file, or git metadata folders.";
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
