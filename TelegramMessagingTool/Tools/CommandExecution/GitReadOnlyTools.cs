using System.Diagnostics;
using System.Text;

namespace TelegramMessagingTool.Tools.CommandExecution;

public abstract class GitReadOnlyTool : IAgentTool
{
    private const int MaxOutputCharacters = 6000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly string _projectRoot;
    private readonly IReadOnlyList<string> _arguments;
    private readonly string _displayCommand;

    protected GitReadOnlyTool(string projectRoot, IReadOnlyList<string> arguments, string displayCommand)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _arguments = arguments;
        _displayCommand = displayCommand;
    }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_projectRoot))
        {
            return ToolResult.Fail($"Project root does not exist: {_projectRoot}");
        }

        if (!IsPathInsideDirectory(_projectRoot, _projectRoot))
        {
            return ToolResult.Fail("Project root failed safety validation.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in _arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            outputBuilder.Append(await outputTask);
            errorBuilder.Append(await errorTask);

            string output = RenderOutput(process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
            return process.ExitCode == 0
                ? ToolResult.Ok(output)
                : ToolResult.Fail(output);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ToolResult.Fail($"Command timed out after {Timeout.TotalSeconds:0} seconds: {_displayCommand}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return ToolResult.Fail($"Could not run {_displayCommand}: {ex.Message}");
        }
    }

    private string RenderOutput(int exitCode, string stdout, string stderr)
    {
        string combined = string.Join("\n", new[]
        {
            $"Command: {_displayCommand}",
            $"Working directory: {_projectRoot}",
            $"Exit code: {exitCode}",
            string.IsNullOrWhiteSpace(stdout) ? string.Empty : "stdout:\n" + stdout.Trim(),
            string.IsNullOrWhiteSpace(stderr) ? string.Empty : "stderr:\n" + stderr.Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return combined.Length <= MaxOutputCharacters
            ? combined
            : combined[..MaxOutputCharacters] + "\n... [truncated]";
    }

    private static bool IsPathInsideDirectory(string candidatePath, string rootPath)
    {
        string candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class GitStatusTool : GitReadOnlyTool
{
    public GitStatusTool(string projectRoot)
        : base(projectRoot, ["status", "--short", "--branch"], "git status --short --branch")
    {
    }

    public override string Name => "git_status";

    public override string Description => "Read-only: shows git branch and short working-tree status for the configured project root.";
}

public sealed class GitDiffTool : GitReadOnlyTool
{
    public GitDiffTool(string projectRoot)
        : base(projectRoot, ["diff", "--", "."], "git diff -- .")
    {
    }

    public override string Name => "git_diff";

    public override string Description => "Read-only: shows unstaged git diff for the configured project root.";
}

public sealed class GitLogRecentTool : GitReadOnlyTool
{
    public GitLogRecentTool(string projectRoot)
        : base(projectRoot, ["log", "--oneline", "-5"], "git log --oneline -5")
    {
    }

    public override string Name => "git_log_recent";

    public override string Description => "Read-only: shows the five most recent git commits for the configured project root.";
}
