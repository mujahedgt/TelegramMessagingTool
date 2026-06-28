namespace TelegramMessagingTool.Plugins;

public sealed class PluginManifestScanner
{
    public PluginScanResult Scan(string pluginDirectory)
    {
        string root = Path.GetFullPath(pluginDirectory);
        var manifests = new List<DiscoveredPluginManifest>();
        var diagnostics = new List<string>();

        if (!Directory.Exists(root))
        {
            return new PluginScanResult(root, manifests, [$"Plugin directory does not exist: {root}"]);
        }

        var seenToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string manifestPath in Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string fullManifestPath = Path.GetFullPath(manifestPath);
            if (!IsPathInsideDirectory(fullManifestPath, root))
            {
                diagnostics.Add($"Skipped manifest outside plugin directory: {fullManifestPath}");
                continue;
            }

            string json;
            try
            {
                json = File.ReadAllText(fullManifestPath);
            }
            catch (IOException ex)
            {
                diagnostics.Add($"Could not read manifest {fullManifestPath}: {ex.Message}");
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                diagnostics.Add($"Could not read manifest {fullManifestPath}: {ex.Message}");
                continue;
            }

            PluginManifestParseResult parseResult = PluginManifest.TryParse(json);
            if (!parseResult.Success || parseResult.Manifest is null)
            {
                diagnostics.Add($"Invalid manifest {fullManifestPath}: {parseResult.Error}");
                continue;
            }

            var duplicateTools = parseResult.Manifest.AllowedToolNames
                .Where(toolName => seenToolNames.Contains(toolName))
                .ToList();
            if (duplicateTools.Count > 0)
            {
                diagnostics.Add($"Skipped plugin '{parseResult.Manifest.Id}' because tool names are duplicated across manifests: {string.Join(", ", duplicateTools)}");
                continue;
            }

            foreach (string toolName in parseResult.Manifest.AllowedToolNames)
            {
                seenToolNames.Add(toolName);
            }

            manifests.Add(new DiscoveredPluginManifest(parseResult.Manifest, fullManifestPath));
        }

        return new PluginScanResult(root, manifests, diagnostics);
    }

    private static bool IsPathInsideDirectory(string candidatePath, string rootPath)
    {
        string candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record DiscoveredPluginManifest(PluginManifest Manifest, string ManifestPath);

public sealed record PluginScanResult(string PluginDirectory, IReadOnlyList<DiscoveredPluginManifest> Manifests, IReadOnlyList<string> Diagnostics)
{
    public int EnabledCount => Manifests.Count(x => x.Manifest.Enabled);

    public int DisabledCount => Manifests.Count(x => !x.Manifest.Enabled);

    public string RenderSummary()
    {
        if (Manifests.Count == 0)
        {
            return Diagnostics.Count == 0
                ? $"No plugin manifests found in {PluginDirectory}."
                : $"No valid plugin manifests found in {PluginDirectory}. Diagnostics: {string.Join("; ", Diagnostics.Take(3))}";
        }

        string manifestSummary = string.Join("; ", Manifests.Select(x => $"{x.Manifest.Id} v{x.Manifest.Version} ({(x.Manifest.Enabled ? "enabled" : "disabled")}, tools: {string.Join(", ", x.Manifest.AllowedToolNames)})"));
        string diagnosticSummary = Diagnostics.Count == 0 ? string.Empty : $" Diagnostics: {string.Join("; ", Diagnostics.Take(3))}";
        return $"Plugin manifests: {Manifests.Count} total, {EnabledCount} enabled, {DisabledCount} disabled. {manifestSummary}.{diagnosticSummary}";
    }
}
