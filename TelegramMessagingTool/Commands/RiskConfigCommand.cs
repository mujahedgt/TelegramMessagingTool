using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class RiskConfigCommand : IBotCommand
{
    private readonly BotSettings _settings;

    public RiskConfigCommand(BotSettings settings)
    {
        _settings = settings;
    }

    public string Name => "/riskconfig";

    public string Description => "Admin-only: show risky runtime feature flags without secrets.";

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

        if (!BotAccessPolicy.IsAdmin(user.ChatId, _settings.AdminChatId))
        {
            return Task.FromResult(new CommandResult(true, BotAccessPolicy.AdminOnlyMessage(_settings.AdminChatId)));
        }

        return Task.FromResult(new CommandResult(true, RenderSummary(_settings)));
    }

    public static string RenderSummary(BotSettings settings)
    {
        return string.Join("\n", new[]
        {
            "Risk configuration summary",
            $"- Public access: {FormatEnabled(settings.AllowPublicAccess, warnWhenEnabled: true)}",
            $"- Message content logging: {FormatEnabled(settings.LogMessageContent, warnWhenEnabled: true)}",
            $"- Repo write tools: {FormatEnabled(settings.EnableRepoWriteTools, warnWhenEnabled: true)}",
            $"- GitHub write tools: {FormatEnabled(settings.GitHub.EnableGitHubWriteTools, warnWhenEnabled: true)}",
            $"- Plugin loading: {FormatEnabled(settings.EnablePlugins, warnWhenEnabled: true)} (trusted local DLLs only)",
            $"- Safe command tools: {FormatEnabled(settings.EnableSafeCommandTools, warnWhenEnabled: true)}",
            $"- Search routing: {settings.SearchRoutingMode}",
            $"- Audio transcription: {FormatProviderGate(settings.EnableAudioTranscription, settings.AudioTranscriptionCommand)}",
            $"- TTS: {FormatProviderGate(settings.EnableTextToSpeech, settings.TextToSpeechCommand)}",
            "",
            "Secrets: token values, database connection strings, and provider credentials are intentionally not shown."
        });
    }

    private static string FormatEnabled(bool enabled, bool warnWhenEnabled)
    {
        if (!enabled)
        {
            return "disabled";
        }

        return warnWhenEnabled ? "ENABLED ⚠" : "ENABLED";
    }

    private static string FormatProviderGate(bool enabled, string command)
    {
        if (!enabled)
        {
            return "disabled";
        }

        return string.IsNullOrWhiteSpace(command)
            ? "enabled, provider command missing"
            : "enabled, provider command configured";
    }
}
