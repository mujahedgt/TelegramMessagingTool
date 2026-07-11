using System.Reflection;
using System.Runtime.Loader;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.Plugins;

public sealed class PluginToolLoader
{
    private readonly PluginManifestScanner _scanner;
    private readonly IReadOnlySet<string> _allowedAssemblyHashes;
    private readonly bool _requireHashAllowlist;

    public PluginToolLoader(
        PluginManifestScanner? scanner = null,
        IReadOnlySet<string>? allowedAssemblyHashes = null,
        bool requireHashAllowlist = false)
    {
        _scanner = scanner ?? new PluginManifestScanner();
        _allowedAssemblyHashes = allowedAssemblyHashes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _requireHashAllowlist = requireHashAllowlist;
    }

    public PluginToolLoadResult LoadEnabledTools(string pluginDirectory, IEnumerable<string> existingToolNames)
    {
        PluginScanResult scanResult = _scanner.Scan(pluginDirectory);
        var registrations = new List<ToolRegistration>();
        var diagnostics = new List<string>(scanResult.Diagnostics);
        var knownToolNames = new HashSet<string>(existingToolNames, StringComparer.OrdinalIgnoreCase);

        foreach (DiscoveredPluginManifest discovered in scanResult.Manifests.Where(x => x.Manifest.Enabled))
        {
            string manifestDirectory = Path.GetDirectoryName(discovered.ManifestPath) ?? scanResult.PluginDirectory;
            string assemblyPath = Path.GetFullPath(Path.Combine(manifestDirectory, discovered.Manifest.EntryAssembly));
            if (!IsPathInsideDirectory(assemblyPath, scanResult.PluginDirectory))
            {
                diagnostics.Add($"Skipped plugin '{discovered.Manifest.Id}' because entry assembly is outside plugin directory: {assemblyPath}");
                continue;
            }

            if (!File.Exists(assemblyPath))
            {
                diagnostics.Add($"Skipped plugin '{discovered.Manifest.Id}' because entry assembly is missing: {assemblyPath}");
                continue;
            }

            string assemblySha256;
            try
            {
                assemblySha256 = PluginTrustDiagnostics.ComputeSha256(assemblyPath);
                diagnostics.Add($"Plugin '{discovered.Manifest.Id}' entry assembly sha256={assemblySha256}");
            }
            catch (IOException ex)
            {
                diagnostics.Add($"Skipped plugin '{discovered.Manifest.Id}' because entry assembly hash could not be computed: {ex.Message}");
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                diagnostics.Add($"Skipped plugin '{discovered.Manifest.Id}' because entry assembly hash could not be computed: {ex.Message}");
                continue;
            }

            if (_requireHashAllowlist && !PluginTrustDiagnostics.IsAllowed(assemblySha256, _allowedAssemblyHashes))
            {
                diagnostics.Add($"Skipped plugin '{discovered.Manifest.Id}' because entry assembly sha256={assemblySha256} is not in PLUGIN_ALLOWED_SHA256.");
                continue;
            }

            Assembly assembly;
            try
            {
                assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                diagnostics.Add($"Skipped plugin '{discovered.Manifest.Id}' because entry assembly could not be loaded: {ex.Message}");
                continue;
            }

            IReadOnlyList<IAgentTool> pluginTools = CreateTools(assembly, discovered.Manifest, diagnostics);
            foreach (IAgentTool tool in pluginTools)
            {
                if (!discovered.Manifest.AllowedToolNames.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
                {
                    diagnostics.Add($"Skipped plugin tool '{tool.Name}' from '{discovered.Manifest.Id}' because it is not listed in allowedToolNames.");
                    continue;
                }

                if (!knownToolNames.Add(tool.Name))
                {
                    diagnostics.Add($"Skipped plugin tool '{tool.Name}' from '{discovered.Manifest.Id}' because a tool with that name is already registered.");
                    continue;
                }

                ToolRiskLevel riskLevel = ParseRiskLevel(discovered.Manifest.RiskLevel);
                registrations.Add(new ToolRegistration(
                    tool,
                    $"plugin:{discovered.Manifest.Id}",
                    riskLevel,
                    discovered.Manifest.IsReadOnly,
                    discovered.Manifest.SafetySummary));
            }
        }

        return new PluginToolLoadResult(scanResult, registrations, diagnostics);
    }

    private static IReadOnlyList<IAgentTool> CreateTools(Assembly assembly, PluginManifest manifest, List<string> diagnostics)
    {
        var tools = new List<IAgentTool>();
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            diagnostics.Add($"Skipped plugin '{manifest.Id}' because exported types could not be read: {ex.Message}");
            return tools;
        }

        foreach (Type type in types.Where(x => typeof(IAgentTool).IsAssignableFrom(x) && x is { IsAbstract: false, IsInterface: false }))
        {
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                diagnostics.Add($"Skipped plugin tool type '{type.FullName}' from '{manifest.Id}' because it has no public parameterless constructor.");
                continue;
            }

            try
            {
                if (Activator.CreateInstance(type) is IAgentTool tool)
                {
                    tools.Add(tool);
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Skipped plugin tool type '{type.FullName}' from '{manifest.Id}' because it could not be created: {ex.Message}");
            }
        }

        return tools;
    }

    private static ToolRiskLevel ParseRiskLevel(string riskLevel)
    {
        return riskLevel.ToLowerInvariant() switch
        {
            "low" => ToolRiskLevel.Low,
            "high" => ToolRiskLevel.High,
            _ => ToolRiskLevel.Medium
        };
    }

    private static bool IsPathInsideDirectory(string candidatePath, string rootPath)
    {
        string candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record PluginToolLoadResult(
    PluginScanResult ScanResult,
    IReadOnlyList<ToolRegistration> Tools,
    IReadOnlyList<string> Diagnostics);
