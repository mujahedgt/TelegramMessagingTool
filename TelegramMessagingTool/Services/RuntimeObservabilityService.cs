using System.Text.RegularExpressions;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.Services;

public sealed partial class RuntimeObservabilityService
{
    private readonly Action<string> _write;

    public RuntimeObservabilityService(Action<string>? write = null)
    {
        _write = write ?? (_ => { });
    }

    public void ToolCallRequested(string toolName, ToolRiskLevel riskLevel, bool isReadOnly)
    {
        Write($"TOOL_CALL tool={SanitizeToken(toolName)} risk={riskLevel.ToString().ToLowerInvariant()} mode={(isReadOnly ? "read-only" : "state-changing")}");
    }

    public void ToolCallCompleted(string toolName, bool success)
    {
        Write($"TOOL_RESULT tool={SanitizeToken(toolName)} success={success.ToString().ToLowerInvariant()}");
    }

    public void PendingActionCreated(int actionId, string toolName, string riskLevel)
    {
        Write($"PENDING_ACTION id={actionId} tool={SanitizeToken(toolName)} risk={SanitizeToken(riskLevel)} status=pending");
    }

    public void PendingActionDecision(int actionId, string toolName, string status)
    {
        Write($"PENDING_DECISION id={actionId} tool={SanitizeToken(toolName)} status={SanitizeToken(status)}");
    }

    public void ApprovalExecutionCompleted(int actionId, string toolName, bool executed, bool success, string message)
    {
        Write($"APPROVAL_EXECUTION id={actionId} tool={SanitizeToken(toolName)} executed={executed.ToString().ToLowerInvariant()} success={success.ToString().ToLowerInvariant()} note={RedactAndTruncate(message, 180)}");
    }

    public void GitHubApiFailure(string operation, string repository, string message)
    {
        Write($"GITHUB_API_FAILURE operation={SanitizeToken(operation)} repo={SanitizeToken(repository)} note={RedactAndTruncate(message, 180)}");
    }

    public void RepoWriteResult(string operation, bool success, string message)
    {
        Write($"REPO_WRITE operation={SanitizeToken(operation)} success={success.ToString().ToLowerInvariant()} note={RedactAndTruncate(message, 180)}");
    }

    private void Write(string detail)
    {
        _write(detail);
    }

    private static string SanitizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        string compact = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        compact = SafeTokenCharacters().Replace(compact, "_");
        return compact.Length <= 80 ? compact : compact[..80];
    }

    private static string RedactAndTruncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        string compact = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        compact = SecretAssignmentPattern().Replace(compact, "$1=[REDACTED]");
        compact = TelegramTokenPattern().Replace(compact, "[REDACTED_TOKEN]");
        compact = GitHubTokenPattern().Replace(compact, "[REDACTED_TOKEN]");
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }

    [GeneratedRegex("[^A-Za-z0-9_.:/=-]+")]
    private static partial Regex SafeTokenCharacters();

    [GeneratedRegex("(?i)\\b([A-Z0-9_]*(?:TOKEN|PASSWORD|SECRET|KEY|CONNECTION)[A-Z0-9_]*)\\s*=\\s*[^\\s;]+")]
    private static partial Regex SecretAssignmentPattern();

    [GeneratedRegex("\\b\\d{6,}:[A-Za-z0-9_-]{20,}\\b")]
    private static partial Regex TelegramTokenPattern();

    [GeneratedRegex("\\b(?:ghp|github_pat|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{10,}\\b")]
    private static partial Regex GitHubTokenPattern();
}
