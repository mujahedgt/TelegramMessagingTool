namespace TelegramMessagingTool;

public static class RuntimeRiskSummary
{
    public static IReadOnlyList<string> RenderStartupWarnings(BotSettings settings, string accessMode)
    {
        var warnings = new List<string>();

        if (accessMode.Equals("public override", StringComparison.OrdinalIgnoreCase) || settings.AllowPublicAccess)
        {
            warnings.Add("ALLOW_PUBLIC_ACCESS is enabled. Anyone who finds the bot can use it.");
        }
        else if (accessMode.Equals("locked", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Telegram access is locked. Set ADMIN_CHAT_ID, ALLOWED_CHAT_IDS, or ALLOW_PUBLIC_ACCESS=true intentionally.");
        }

        if (settings.LogMessageContent)
        {
            warnings.Add("LOG_MESSAGE_CONTENT is enabled. Logs may contain private chat text.");
        }

        if (settings.EnableRepoWriteTools)
        {
            warnings.Add("Repo write tools are enabled. Approved actions can modify the local repository.");
        }

        if (settings.GitHub.EnableGitHubWriteTools)
        {
            warnings.Add("GitHub write tools are enabled. Approved actions can create GitHub issues/comments in allowed repositories.");
        }

        if (settings.EnablePlugins)
        {
            warnings.Add("Plugin loading is enabled. Only trusted local DLL plugins should be present in the plugin directory.");
        }

        if (settings.EnableSafeCommandTools)
        {
            warnings.Add("Safe command tools are enabled. The model can request allowlisted local read/test commands.");
        }

        if (settings.SearchRoutingMode.Equals("llm", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Search routing mode is llm. The model can classify when live search should run.");
        }

        if (settings.EnableAudioTranscription && string.IsNullOrWhiteSpace(settings.AudioTranscriptionCommand))
        {
            warnings.Add("Audio transcription is enabled, but the provider command is missing.");
        }

        if (settings.EnableImageOcr && string.IsNullOrWhiteSpace(settings.ImageOcrCommand))
        {
            warnings.Add("Image OCR is enabled, but the provider command is missing.");
        }

        if (settings.EnableTextToSpeech && string.IsNullOrWhiteSpace(settings.TextToSpeechCommand))
        {
            warnings.Add("TTS is enabled, but the provider command is missing.");
        }

        return warnings;
    }

    public static string RenderRiskConfig(BotSettings settings)
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
            $"- Image OCR: {FormatProviderGate(settings.EnableImageOcr, settings.ImageOcrCommand)}",
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
