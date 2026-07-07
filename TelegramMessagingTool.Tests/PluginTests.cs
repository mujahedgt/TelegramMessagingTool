using static TestAssert;
using TelegramMessagingTool.Plugins;
using TelegramMessagingTool.Tools;

public static class PluginTests
{
    public static void RunPluginManifestCompatibilityTests()
    {
        PluginManifestParseResult compatibleResult = PluginManifest.TryParse("""
        {
          "id": "compatible-plugin",
          "name": "Compatible Plugin",
          "version": "1.2.3",
          "apiVersion": "1.0",
          "entryAssembly": "CompatiblePlugin.dll",
          "enabled": true,
          "allowedToolNames": ["compatible_tool"]
        }
        """);
        AssertTrue(compatibleResult.Success, "PluginManifest accepts current API version manifests");
        AssertEqual("1.0", compatibleResult.Manifest?.ApiVersion, "PluginManifest stores API version metadata");
        AssertEqual(0, compatibleResult.Warnings.Count, "PluginManifest does not warn for explicit compatible API version");

        PluginManifestParseResult missingApiResult = PluginManifest.TryParse("""
        {
          "id": "legacy-plugin",
          "name": "Legacy Plugin",
          "version": "1.0.0",
          "entryAssembly": "LegacyPlugin.dll",
          "enabled": true,
          "allowedToolNames": ["legacy_tool"]
        }
        """);
        AssertTrue(missingApiResult.Success, "PluginManifest accepts missing apiVersion for backward compatibility");
        AssertEqual(PluginManifest.SupportedApiVersion, missingApiResult.Manifest?.ApiVersion, "PluginManifest defaults missing apiVersion to the supported API version");
        AssertTrue(missingApiResult.Warnings.Any(x => x.Contains("missing apiVersion", StringComparison.OrdinalIgnoreCase)), "PluginManifest warns when apiVersion is missing");

        PluginManifestParseResult futureMajorResult = PluginManifest.TryParse("""
        {
          "id": "future-plugin",
          "name": "Future Plugin",
          "version": "2.0.0",
          "apiVersion": "2.0",
          "entryAssembly": "FuturePlugin.dll",
          "enabled": true,
          "allowedToolNames": ["future_tool"]
        }
        """);
        AssertFalse(futureMajorResult.Success, "PluginManifest rejects incompatible future major API versions");
        AssertTrue(futureMajorResult.Error.Contains("apiVersion", StringComparison.OrdinalIgnoreCase), "PluginManifest incompatible API error names apiVersion");
    }

    public static async Task RunSamplePluginToolTestsAsync()
    {
        PluginToolLoadResult samplePluginLoadResult = new PluginToolLoader().LoadEnabledTools(
            Path.Combine(Directory.GetCurrentDirectory(), "plugins"),
            ["datetime"]);
        ToolRegistration samplePluginRegistration = samplePluginLoadResult.Tools.Single(x => x.Tool.Name == "sample_echo");
        ToolRegistration dotnetProjectPluginRegistration = samplePluginLoadResult.Tools.Single(x => x.Tool.Name == "dotnet_create_project");
        AssertTrue(samplePluginRegistration.Source.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase), "PluginToolLoader marks plugin tool source");
        AssertEqual(ToolRiskLevel.Medium, dotnetProjectPluginRegistration.RiskLevel, "PluginToolLoader applies manifest risk metadata to the .NET project plugin tool");
        AssertFalse(dotnetProjectPluginRegistration.IsReadOnly, "PluginToolLoader applies state-changing metadata to the .NET project plugin tool");
        AssertTrue(dotnetProjectPluginRegistration.SafetySummary.Contains("GeneratedProjects", StringComparison.OrdinalIgnoreCase), "PluginToolLoader applies sandbox safety summary metadata to plugin tools");

        string generatedProjectTestRoot = Path.Combine(Path.GetTempPath(), $"TelegramMessagingTool_GeneratedProjects_{Guid.NewGuid():N}");
        string originalCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.CreateDirectory(generatedProjectTestRoot);
            Environment.CurrentDirectory = generatedProjectTestRoot;
            ToolResult generatedProjectResult = await dotnetProjectPluginRegistration.Tool.ExecuteAsync("{\"name\":\"DemoPluginApp\"}", CancellationToken.None);
            AssertTrue(generatedProjectResult.Success, "dotnet_create_project creates a sandboxed sample project from JSON input");
            AssertTrue(File.Exists(Path.Combine(generatedProjectTestRoot, "GeneratedProjects", "DemoPluginApp", "DemoPluginApp.csproj")), "dotnet_create_project writes the generated csproj under GeneratedProjects");
            AssertTrue(File.Exists(Path.Combine(generatedProjectTestRoot, "GeneratedProjects", "DemoPluginApp", "Program.cs")), "dotnet_create_project writes Program.cs under GeneratedProjects");

            ToolResult duplicateProjectResult = await dotnetProjectPluginRegistration.Tool.ExecuteAsync("{\"name\":\"DemoPluginApp\"}", CancellationToken.None);
            AssertFalse(duplicateProjectResult.Success, "dotnet_create_project rejects existing non-empty project folders");

            ToolResult nearestFridayProjectResult = await dotnetProjectPluginRegistration.Tool.ExecuteAsync("{\"name\":\"NearestFridayApp\",\"template\":\"nearest_friday\"}", CancellationToken.None);
            AssertTrue(nearestFridayProjectResult.Success, "dotnet_create_project creates a nearest-Friday console project template from JSON input");
            string nearestFridayProgram = await File.ReadAllTextAsync(Path.Combine(generatedProjectTestRoot, "GeneratedProjects", "NearestFridayApp", "Program.cs"), CancellationToken.None);
            AssertTrue(nearestFridayProgram.Contains("DateTime.Today", StringComparison.OrdinalIgnoreCase), "nearest-Friday template takes today's date");
            AssertTrue(nearestFridayProgram.Contains("DayOfWeek.Friday", StringComparison.OrdinalIgnoreCase), "nearest-Friday template targets Friday explicitly");
            AssertTrue(nearestFridayProgram.Contains("% 7", StringComparison.OrdinalIgnoreCase), "nearest-Friday template uses modulo day offset logic");
            AssertFalse(nearestFridayProgram.Contains("dayOfWeek >= 4", StringComparison.OrdinalIgnoreCase), "nearest-Friday template avoids the broken Thursday-or-later logic");

            ToolResult unsupportedTemplateProjectResult = await dotnetProjectPluginRegistration.Tool.ExecuteAsync("{\"name\":\"BadTemplateApp\",\"template\":\"danger\"}", CancellationToken.None);
            AssertFalse(unsupportedTemplateProjectResult.Success, "dotnet_create_project rejects unsupported templates");

            ToolResult blockedTraversalProjectResult = await dotnetProjectPluginRegistration.Tool.ExecuteAsync("../Bad", CancellationToken.None);
            AssertFalse(blockedTraversalProjectResult.Success, "dotnet_create_project rejects traversal-like project names");
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            if (Directory.Exists(generatedProjectTestRoot))
            {
                Directory.Delete(generatedProjectTestRoot, recursive: true);
            }
        }

        ToolRegistry pluginSourceRegistry = new([new DateTimeTool()], samplePluginLoadResult.Tools);
        string pluginToolList = pluginSourceRegistry.RenderToolList();
        string pluginToolInstructions = pluginSourceRegistry.RenderToolInstructions();
        AssertTrue(pluginToolList.Contains("source: plugin:", StringComparison.OrdinalIgnoreCase), "ToolRegistry renders plugin source in /tools output");
        AssertTrue(pluginToolList.Contains("dotnet_create_project", StringComparison.OrdinalIgnoreCase), "ToolRegistry renders the sample .NET project plugin tool in /tools output");
        AssertTrue(pluginToolList.Contains("risk: medium", StringComparison.OrdinalIgnoreCase), "ToolRegistry renders plugin risk metadata in /tools output");
        AssertTrue(pluginToolList.Contains("can change state", StringComparison.OrdinalIgnoreCase), "ToolRegistry renders plugin state-changing metadata in /tools output");
        AssertTrue(pluginToolInstructions.Contains("dotnet_create_project", StringComparison.OrdinalIgnoreCase), "ToolRegistry model instructions include dotnet_create_project when the plugin is loaded");
        AssertTrue(pluginToolInstructions.Contains("nearest_friday", StringComparison.OrdinalIgnoreCase), "ToolRegistry instructions tell the model how to request the nearest-Friday console-project template");
    }
}
