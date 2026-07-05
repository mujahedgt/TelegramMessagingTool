using System.Text.Json;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public static class PendingActionPreviewFormatter
{
    public static string RenderListItem(PendingAction action)
    {
        string summary = RenderPayloadSummary(action.PayloadJson);
        return $"""
#{action.Id} [{action.Status}] {action.ToolName}
Exact risk: {Normalize(action.RiskLevel)}
{action.Description}
{summary}
Expires UTC: {action.ExpiresAt:yyyy-MM-dd HH:mm}
Approve: /approve {action.Id}
Deny: /deny {action.Id}
Details: /action {action.Id}
""".TrimEnd();
    }

    public static string RenderDetails(PendingAction action)
    {
        string decisionTime = action.DecidedAt is null
            ? "not decided"
            : action.DecidedAt.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        string decisionNote = string.IsNullOrWhiteSpace(action.DecisionNote)
            ? "none"
            : action.DecisionNote;
        string summary = RenderPayloadSummary(action.PayloadJson);

        return $"""
Action #{action.Id}

Type: {action.ToolName}
Exact risk: {Normalize(action.RiskLevel)}
Status: {action.Status}
Description: {action.Description}

Created UTC: {action.CreatedAt:yyyy-MM-dd HH:mm:ss}
Expires UTC: {action.ExpiresAt:yyyy-MM-dd HH:mm:ss}
Decided UTC: {decisionTime}

Decision note:
{decisionNote}

Payload summary:
{summary}
""".TrimEnd();
    }

    public static string RenderPayloadSummary(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return "Payload summary: none";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return "Payload summary: non-object payload";
            }

            string action = ReadString(root, "action");
            return action switch
            {
                "repo_replace_text" => RenderRepoReplaceText(root),
                "repo_apply_patch" => RenderRepoApplyPatch(root),
                "repo_commit_changes" => RenderRepoCommit(root),
                "repo_push_changes" => RenderRepoPush(root),
                "publish_release" => RenderRelease(root, "Publish release"),
                "restart_latest_bot" => RenderRelease(root, "Restart latest release"),
                "github_create_issue" => RenderGitHubCreateIssue(root),
                "github_comment_issue" => RenderGitHubCommentIssue(root),
                _ => RenderGeneric(root)
            };
        }
        catch (JsonException)
        {
            return "Payload summary: invalid JSON payload";
        }
    }

    private static string RenderRepoReplaceText(JsonElement root)
    {
        string path = ReadString(root, "path");
        string oldText = ReadString(root, "old_text");
        string newText = ReadString(root, "new_text");
        string reason = ReadString(root, "reason");
        return string.Join('\n', NonEmpty([
            $"Target file: {Fallback(path, "unknown")}",
            $"Diff summary: -{oldText.Length}/+{newText.Length} chars",
            $"Reason: {Fallback(reason, "not provided")}"
        ]));
    }

    private static string RenderRepoApplyPatch(JsonElement root)
    {
        string patch = ReadString(root, "patch");
        string reason = ReadString(root, "reason");
        IReadOnlyList<string> paths = ExtractPatchPaths(patch);
        return string.Join('\n', NonEmpty([
            $"Patch files: {(paths.Count == 0 ? "unknown" : string.Join(", ", paths.Take(8)))}",
            $"Diff summary: {CountLinesStartingWith(patch, '+')} additions / {CountLinesStartingWith(patch, '-')} deletions",
            $"Reason: {Fallback(reason, "not provided")}"
        ]));
    }

    private static string RenderRepoCommit(JsonElement root)
    {
        string message = ReadString(root, "message");
        return $"Git command preview: git commit -m \"{TruncateInline(message, 100)}\"";
    }

    private static string RenderRepoPush(JsonElement root)
    {
        string branch = ReadString(root, "branch");
        return $"Git command preview: git push origin {Fallback(branch, "<current-branch>")}";
    }

    private static string RenderRelease(JsonElement root, string title)
    {
        string projectRoot = ReadString(root, "project_root");
        string reason = ReadString(root, "reason");
        return string.Join('\n', NonEmpty([
            $"Action preview: {title}",
            $"Project root: {Fallback(projectRoot, "unknown")}",
            $"Reason: {Fallback(reason, "not provided")}"
        ]));
    }

    private static string RenderGitHubCreateIssue(JsonElement root)
    {
        string owner = ReadString(root, "owner");
        string repo = ReadString(root, "repo");
        string title = ReadString(root, "title");
        string labels = RenderStringArray(root, "labels");
        return string.Join('\n', NonEmpty([
            $"GitHub repository: {owner}/{repo}",
            $"GitHub issue preview: create issue \"{TruncateInline(title, 160)}\"",
            $"Labels: {Fallback(labels, "none")}"
        ]));
    }

    private static string RenderGitHubCommentIssue(JsonElement root)
    {
        string owner = ReadString(root, "owner");
        string repo = ReadString(root, "repo");
        int number = ReadInt(root, "number");
        string body = ReadString(root, "body");
        return string.Join('\n', NonEmpty([
            $"GitHub repository: {owner}/{repo}",
            $"GitHub issue preview: comment on #{number}",
            $"Comment length: {body.Length} chars"
        ]));
    }

    private static string RenderGeneric(JsonElement root)
    {
        string action = ReadString(root, "action");
        string reason = ReadString(root, "reason");
        return string.Join('\n', NonEmpty([
            $"Action preview: {Fallback(action, "unknown")}",
            $"Reason: {Fallback(reason, "not provided")}"
        ]));
    }

    private static IReadOnlyList<string> ExtractPatchPaths(string patch)
    {
        return patch.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("+++ b/", StringComparison.Ordinal) || line.StartsWith("--- a/", StringComparison.Ordinal))
            .Select(line => line[6..].Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path) && path != "/dev/null")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CountLinesStartingWith(string text, char marker)
    {
        return text.Split('\n').Count(line => line.StartsWith(marker) && !line.StartsWith($"{marker}{marker}{marker}", StringComparison.Ordinal));
    }

    private static string ReadString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value)
            ? value
            : 0;
    }

    private static string RenderStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(", ", element.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static IEnumerable<string> NonEmpty(IEnumerable<string> lines)
    {
        return lines.Where(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string Fallback(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();

    private static string TruncateInline(string value, int maxLength)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }
}
