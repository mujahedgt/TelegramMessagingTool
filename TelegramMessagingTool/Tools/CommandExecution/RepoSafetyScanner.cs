using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TelegramMessagingTool.Tools.CommandExecution;

public static partial class RepoSafetyScanner
{
    private const int TimeoutMilliseconds = 30_000;

    private static readonly string[] BlockedExactFileNames = [
        ".env",
        ".env.local",
        ".env.production",
        "secrets.json",
        "appsettings.production.json",
        "appsettings.prod.json"
    ];

    private static readonly string[] BlockedPathSegments = [
        ".git",
        "release",
        "bin",
        "obj",
        "UserFiles",
        "ImportInbox"
    ];

    private static readonly string[] BlockedExtensions = [
        ".pfx",
        ".pem",
        ".key",
        ".crt",
        ".cer",
        ".sqlite",
        ".sqlite3",
        ".db",
        ".mdf",
        ".ldf",
        ".bak",
        ".exe",
        ".dll",
        ".pdb",
        ".zip",
        ".7z",
        ".rar"
    ];

    public static RepoSafetyScanResult ScanChangedPaths(IEnumerable<string> paths)
    {
        foreach (string rawPath in paths)
        {
            string path = NormalizePath(rawPath);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fileName = Path.GetFileName(path);
            if (BlockedExactFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                return RepoSafetyScanResult.Blocked($"Safety scan refused blocked secret/config file: {path}");
            }

            string extension = Path.GetExtension(path);
            if (BlockedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return RepoSafetyScanResult.Blocked($"Safety scan refused binary, database, certificate, backup, or release artifact: {path}");
            }

            string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Any(segment => BlockedPathSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            {
                return RepoSafetyScanResult.Blocked($"Safety scan refused generated/runtime/release path: {path}");
            }
        }

        return RepoSafetyScanResult.Ok("Safety scan passed: no blocked paths detected.");
    }

    public static RepoSafetyScanResult ScanDiff(string diff)
    {
        if (string.IsNullOrWhiteSpace(diff))
        {
            return RepoSafetyScanResult.Ok("Safety scan passed: no diff content to inspect.");
        }

        foreach (string rawLine in diff.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith('+') || line.StartsWith("+++", StringComparison.Ordinal))
            {
                continue;
            }

            string added = line[1..].Trim();
            foreach ((string Name, Regex Pattern) in SecretPatterns())
            {
                if (Pattern.IsMatch(added))
                {
                    return RepoSafetyScanResult.Blocked($"Safety scan refused diff because it contains secret-like pattern: {Name}");
                }
            }
        }

        return RepoSafetyScanResult.Ok("Safety scan passed: no token-like additions detected.");
    }

    public static async Task<RepoSafetyScanResult> ScanRepositoryAsync(string projectRoot, CancellationToken cancellationToken)
    {
        string normalizedRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(normalizedRoot))
        {
            return RepoSafetyScanResult.Blocked($"Safety scan failed: project root does not exist: {normalizedRoot}");
        }

        ProcessCommandResult status = await RunGitAsync(normalizedRoot, ["status", "--porcelain"], cancellationToken);
        if (!status.Success)
        {
            string gitFailure = status.Output + status.Error;
            if (gitFailure.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
            {
                return RepoSafetyScanResult.Ok("Safety scan skipped: project root is not a git repository.");
            }

            return RepoSafetyScanResult.Blocked("Safety scan failed: could not read git status. " + status.RenderForMessage());
        }

        RepoSafetyScanResult pathScan = ScanChangedPaths(ParsePorcelainPaths(status.Output));
        if (!pathScan.Allowed)
        {
            return pathScan;
        }

        ProcessCommandResult diff = await RunGitAsync(normalizedRoot, ["diff", "--no-ext-diff"], cancellationToken);
        if (!diff.Success)
        {
            return RepoSafetyScanResult.Blocked("Safety scan failed: could not read git diff. " + diff.RenderForMessage());
        }

        RepoSafetyScanResult diffScan = ScanDiff(diff.Output);
        if (!diffScan.Allowed)
        {
            return diffScan;
        }

        return RepoSafetyScanResult.Ok("Safety scan passed: changed paths and diff additions look safe.");
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
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            await Task.WhenAll(outputTask, errorTask);
            string output = outputTask.Result;
            string error = errorTask.Result;
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

    private static string NormalizePath(string path) => path.Trim().Trim('"').Replace('\\', '/');

    private static IEnumerable<(string Name, Regex Pattern)> SecretPatterns()
    {
        yield return ("TELEGRAM_BOT_TOKEN", TelegramBotTokenRegex());
        yield return ("GITHUB_TOKEN", GitHubTokenRegex());
        yield return ("connection string password", ConnectionStringPasswordRegex());
        yield return ("private key", PrivateKeyRegex());
        yield return ("generic API key/token assignment", GenericSecretAssignmentRegex());
    }

    [GeneratedRegex(@"(?i)TELEGRAM_BOT_TOKEN\s*[:=]\s*[^\s'\"";]{20,}")]
    private static partial Regex TelegramBotTokenRegex();

    [GeneratedRegex(@"(?i)(ghp|github_pat)_[A-Za-z0-9_]{20,}")]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex(@"(?i)(Password|Pwd)\s*=\s*[^;\s]{6,}")]
    private static partial Regex ConnectionStringPasswordRegex();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(@"(?i)(api[_-]?key|secret|token|password)\s*[:=]\s*['\""`]?[A-Za-z0-9_\-\.]{24,}")]
    private static partial Regex GenericSecretAssignmentRegex();
}

public sealed record RepoSafetyScanResult(bool Allowed, string Message)
{
    public static RepoSafetyScanResult Ok(string message) => new(true, message);

    public static RepoSafetyScanResult Blocked(string message) => new(false, message);
}
