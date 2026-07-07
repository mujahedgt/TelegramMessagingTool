using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Plugins;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class HealthCommand : IBotCommand
{
    private static readonly DateTimeOffset ProcessStartedAtUtc = DateTimeOffset.UtcNow;

    private readonly BotSettings _settings;
    private readonly DocumentStorageService _documentStorage;
    private readonly string _importDirectory;
    private readonly PluginManifestScanner _pluginScanner;

    public HealthCommand(
        BotSettings settings,
        DocumentStorageService documentStorage,
        string importDirectory,
        PluginManifestScanner? pluginScanner = null)
    {
        _settings = settings;
        _documentStorage = documentStorage;
        _importDirectory = Path.GetFullPath(importDirectory);
        _pluginScanner = pluginScanner ?? new PluginManifestScanner();
    }

    public string Name => "/health";

    public string Description => "Show compact runtime health diagnostics without secrets.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!CommandParser.Matches(message.Text, Name))
        {
            return new CommandResult(false, null);
        }

        bool databaseReady = await CanConnectAsync(dbContext, cancellationToken);
        string migrationStatus = await GetMigrationStatusAsync(dbContext, cancellationToken);
        string uptime = FormatDuration(DateTimeOffset.UtcNow - ProcessStartedAtUtc);
        var modelRoutingService = new ModelRoutingService(_settings);
        PluginScanResult pluginScanResult = _pluginScanner.Scan(_settings.PluginDirectory);
        IReadOnlyList<string> riskWarnings = RuntimeRiskSummary.RenderStartupWarnings(
            _settings,
            BotAccessPolicy.DescribeAccessMode(_settings.AllowedChatIds, _settings.AdminChatId, _settings.AllowPublicAccess));

        string reply = $"""
Health

Uptime: {uptime}
Database: {(databaseReady ? "OK" : "Unavailable")}
Migrations: {migrationStatus}
Model routes: {modelRoutingService.RenderSummary()}
Online search: {(_settings.EnableOnlineSearch ? "enabled" : "disabled")}
Search routing: {_settings.SearchRoutingMode}
Plugins: {(_settings.EnablePlugins ? "enabled" : "disabled")}; valid manifests {pluginScanResult.Manifests.Count}, enabled {pluginScanResult.EnabledCount}, diagnostics {pluginScanResult.Diagnostics.Count}
Document storage: {(Directory.Exists(_documentStorage.RootDirectory) ? "OK" : "missing")} ({_documentStorage.RootDirectory})
Import inbox: {(Directory.Exists(_importDirectory) ? "OK" : "missing")} ({_importDirectory})
Risk warnings: {riskWarnings.Count}

Secrets: not shown.
""";

        return new CommandResult(true, reply);
    }

    private static async Task<bool> CanConnectAsync(TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            return false;
        }
    }

    private static async Task<string> GetMigrationStatusAsync(TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<string> pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            return pendingMigrations.Count == 0
                ? "up to date"
                : $"{pendingMigrations.Count} pending";
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return "not available";
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }

        return $"{Math.Max(0, (int)duration.TotalSeconds)}s";
    }
}
