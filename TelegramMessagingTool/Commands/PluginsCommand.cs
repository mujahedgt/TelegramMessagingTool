using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Plugins;

namespace TelegramMessagingTool.Commands;

public sealed class PluginsCommand : IBotCommand
{
    private readonly BotSettings _settings;
    private readonly PluginManifestScanner _scanner;

    public PluginsCommand(BotSettings settings, PluginManifestScanner? scanner = null)
    {
        _settings = settings;
        _scanner = scanner ?? new PluginManifestScanner();
    }

    public string Name => "/plugins";

    public string Description => "Show read-only plugin manifest discovery status and diagnostics.";

    public Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!CommandParser.Matches(message.Text, Name))
        {
            return Task.FromResult(new CommandResult(false, null));
        }

        PluginScanResult scanResult = _scanner.Scan(_settings.PluginDirectory);
        string mode = _settings.EnablePlugins ? "enabled" : "disabled";
        string assemblyLoading = _settings.EnablePlugins
            ? "enabled. Plugin DLLs are trusted OS-level code and are loaded only from enabled manifests in this directory."
            : "disabled. Set ENABLE_PLUGINS=true only for trusted local plugin DLLs.";
        string reply = $"""
Plugin manifest discovery

Mode: {mode}
Directory: {scanResult.PluginDirectory}
Valid manifests: {scanResult.Manifests.Count}
Enabled manifests: {scanResult.EnabledCount}
Disabled manifests: {scanResult.DisabledCount}

Assembly loading: {assemblyLoading}
""";

        if (scanResult.Manifests.Count > 0)
        {
            reply += "\n\nManifests:\n" + string.Join("\n", scanResult.Manifests.Select(RenderManifest));
        }
        else
        {
            reply += "\n\nNo valid plugin manifests found.";
        }

        if (scanResult.Diagnostics.Count > 0)
        {
            reply += "\n\nDiagnostics:\n" + string.Join("\n", scanResult.Diagnostics.Take(10).Select(x => $"- {x}"));
        }

        return Task.FromResult(new CommandResult(true, reply));
    }

    private static string RenderManifest(DiscoveredPluginManifest discovered)
    {
        PluginManifest manifest = discovered.Manifest;
        string enabled = manifest.Enabled ? "enabled" : "disabled";
        string manifestDirectory = Path.GetDirectoryName(discovered.ManifestPath) ?? string.Empty;
        string entryAssemblyPath = Path.GetFullPath(Path.Combine(manifestDirectory, manifest.EntryAssembly));
        string assemblyStatus = File.Exists(entryAssemblyPath) ? "present" : "missing";
        return $"- {manifest.Id} v{manifest.Version} ({enabled}, risk: {manifest.RiskLevel}, tools: {string.Join(", ", manifest.AllowedToolNames)})\n"
            + $"  manifest: {discovered.ManifestPath}\n"
            + $"  entry assembly: {manifest.EntryAssembly} ({assemblyStatus})";
    }
}
