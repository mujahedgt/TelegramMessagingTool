using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TelegramMessagingTool.Tools.CommandExecution;

public sealed class RunDotnetTestsTool : IAgentTool
{
    private const int MaxOutputCharacters = 12000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);
    private readonly string _projectRoot;

    public RunDotnetTestsTool(string projectRoot)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    public string Name => "run_dotnet_tests";

    public string Description => "Fixed command: runs the helper test project only. Input must be {\"target\":\"helper-tests\"}.";

    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        if (!TryParseTarget(input, out string? target, out string? parseError))
        {
            return ToolResult.Fail(parseError ?? "Invalid test target.");
        }

        if (!string.Equals(target, "helper-tests", StringComparison.OrdinalIgnoreCase))
        {
            return ToolResult.Fail("Unsupported test target. Allowed target: helper-tests.");
        }

        if (!Directory.Exists(_projectRoot))
        {
            return ToolResult.Fail($"Project root does not exist: {_projectRoot}");
        }

        string testProject = Path.GetFullPath(Path.Combine(_projectRoot, "TelegramMessagingTool.Tests", "TelegramMessagingTool.Tests.csproj"));
        if (!IsPathInsideDirectory(testProject, _projectRoot))
        {
            return ToolResult.Fail("Test project failed safety validation.");
        }

        if (!File.Exists(testProject))
        {
            return ToolResult.Fail($"Test project not found: {testProject}");
        }

        string displayCommand = "dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo";
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = _projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in new[]
        {
            "run",
            "--project",
            testProject,
            "--configuration",
            "Release",
            "--nologo"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            string output = RenderOutput(displayCommand, process.ExitCode, await outputTask, await errorTask);
            return process.ExitCode == 0
                ? ToolResult.Ok(output)
                : ToolResult.Fail(output);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ToolResult.Fail($"Command timed out after {Timeout.TotalSeconds:0} seconds: {displayCommand}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return ToolResult.Fail($"Could not run {displayCommand}: {ex.Message}");
        }
    }

    private static bool TryParseTarget(string input, out string? target, out string? error)
    {
        target = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Input must be strict JSON: {\"target\":\"helper-tests\"}.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("target", out JsonElement targetElement)
                || targetElement.ValueKind != JsonValueKind.String)
            {
                error = "Input must include string property target. Allowed value: helper-tests.";
                return false;
            }

            target = targetElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                error = "Test target cannot be empty. Allowed value: helper-tests.";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            error = "Input must be strict JSON: {\"target\":\"helper-tests\"}.";
            return false;
        }
    }

    private string RenderOutput(string displayCommand, int exitCode, string stdout, string stderr)
    {
        var lines = new List<string>
        {
            $"Command: {displayCommand}",
            $"Working directory: {_projectRoot}",
            $"Exit code: {exitCode}"
        };

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            lines.Add("stdout:\n" + stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            lines.Add("stderr:\n" + stderr.Trim());
        }

        string combined = string.Join("\n", lines);
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
