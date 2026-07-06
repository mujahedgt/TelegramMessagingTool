using System.Text.Json;
using System.Text.RegularExpressions;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.SamplePlugin;

public sealed class DotNetProjectCreateTool : IAgentTool
{
    private static readonly Regex ProjectNamePattern = new("^[A-Za-z][A-Za-z0-9_.-]{0,63}$", RegexOptions.Compiled);

    public string Name => "dotnet_create_project";

    public string Description => "Sample trusted plugin tool that creates a minimal .NET console project under GeneratedProjects. Input can be a project name or JSON {\"name\":\"DemoApp\",\"template\":\"basic\"}; use template \"nearest_friday\" for a console app that prints today's date and the nearest Friday.";

    public bool RequiresApproval => false;

    public ToolRiskLevel RiskLevel => ToolRiskLevel.Medium;

    public bool IsReadOnly => false;

    public string SafetySummary => "Creates a small project only under the local GeneratedProjects sandbox and refuses overwrite/traversal paths.";

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        ProjectCreateRequest request;
        try
        {
            request = ParseRequest(input);
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(ex.Message);
        }

        string rootDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "GeneratedProjects"));
        string projectDirectory = Path.GetFullPath(Path.Combine(rootDirectory, request.Name));
        if (!IsPathInsideDirectory(projectDirectory, rootDirectory))
        {
            return ToolResult.Fail("Project path escaped the GeneratedProjects sandbox.");
        }

        if (Directory.Exists(projectDirectory) && Directory.EnumerateFileSystemEntries(projectDirectory).Any())
        {
            return ToolResult.Fail($"Project '{request.Name}' already exists and is not empty: {projectDirectory}");
        }

        Directory.CreateDirectory(projectDirectory);
        string safeNamespace = BuildNamespace(request.Name);
        string csprojPath = Path.Combine(projectDirectory, request.Name + ".csproj");
        string programPath = Path.Combine(projectDirectory, "Program.cs");
        string readmePath = Path.Combine(projectDirectory, "README.md");

        await File.WriteAllTextAsync(csprojPath, BuildProjectFile(), cancellationToken);
        await File.WriteAllTextAsync(programPath, BuildProgramFile(safeNamespace, request.Template), cancellationToken);
        await File.WriteAllTextAsync(readmePath, BuildReadme(request.Name, request.Template), cancellationToken);

        return ToolResult.Ok($"Created .NET console project '{request.Name}' under {projectDirectory}\nTemplate: {request.Template}\nFiles: {request.Name}.csproj, Program.cs, README.md\nRun: dotnet run --project \"{csprojPath}\"");
    }

    private static ProjectCreateRequest ParseRequest(string input)
    {
        string value = input?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Input must be a project name or JSON like {\"name\":\"DemoApp\"}.");
        }

        string template = "basic";
        if (value.StartsWith('{'))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(value);
                if (document.RootElement.TryGetProperty("name", out JsonElement nameElement)
                    && nameElement.ValueKind == JsonValueKind.String)
                {
                    value = nameElement.GetString()?.Trim() ?? string.Empty;
                }
                else
                {
                    throw new ArgumentException("JSON input must include a string 'name' property.");
                }

                if (document.RootElement.TryGetProperty("template", out JsonElement templateElement)
                    && templateElement.ValueKind == JsonValueKind.String)
                {
                    template = NormalizeTemplate(templateElement.GetString());
                }
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Input JSON is invalid: " + ex.Message, ex);
            }
        }

        if (!ProjectNamePattern.IsMatch(value) || value.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Project name must start with a letter and contain only letters, numbers, dot, underscore, or dash, up to 64 characters.");
        }

        return new ProjectCreateRequest(value, template);
    }

    private static string NormalizeTemplate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "basic";
        }

        string normalized = value.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "basic" or "console" => "basic",
            "nearest_friday" or "nearestfriday" => "nearest_friday",
            _ => throw new ArgumentException("Unsupported template. Use 'basic' or 'nearest_friday'.")
        };
    }

    private static string BuildProjectFile()
    {
        return """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
""";
    }

    private static string BuildProgramFile(string safeNamespace, string template)
    {
        if (template == "nearest_friday")
        {
            return $$"""
namespace {{safeNamespace}};

public static class Program
{
    public static void Main(string[] args)
    {
        DateTime today = DateTime.Today;
        DateTime nearestFriday = GetNearestFriday(today);

        Console.WriteLine($"Today: {today:dddd, MMMM dd, yyyy}");
        Console.WriteLine($"Nearest Friday: {nearestFriday:dddd, MMMM dd, yyyy}");
    }

    private static DateTime GetNearestFriday(DateTime date)
    {
        int daysUntilFriday = ((int)DayOfWeek.Friday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(daysUntilFriday);
    }
}
""";
        }

        return $$"""
namespace {{safeNamespace}};

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello from {{safeNamespace}}!");
    }
}
""";
    }

    private static string BuildReadme(string projectName, string template)
    {
        string description = template == "nearest_friday"
            ? "This console app takes today's date and prints the nearest upcoming Friday, including today when today is Friday."
            : "This console app prints a small hello message.";

        return $$"""
# {{projectName}}

Generated by the TelegramMessagingTool sample plugin tool `dotnet_create_project`.

{{description}}

## Run

```bash
dotnet run --project "{{projectName}}.csproj"
```
""";
    }

    private static string BuildNamespace(string projectName)
    {
        string safe = Regex.Replace(projectName, "[^A-Za-z0-9_]", "_");
        return char.IsLetter(safe[0]) ? safe : "Generated_" + safe;
    }

    private static bool IsPathInsideDirectory(string candidatePath, string rootPath)
    {
        string candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ProjectCreateRequest(string Name, string Template);
}
