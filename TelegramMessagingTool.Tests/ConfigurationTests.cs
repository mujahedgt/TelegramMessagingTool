using static TestAssert;
using TelegramMessagingTool;

public static class ConfigurationTests
{
    public static void RunConfigurationTests()
    {
        var allowlist = BotAccessPolicy.ParseAllowedChatIds("123, 456, invalid,789");
        var emptyAllowlist = new HashSet<long>();
        AssertTrue(BotAccessPolicy.IsAllowed(123, allowlist, adminChatId: 777, allowPublicAccess: false), "allowlist includes 123");
        AssertTrue(BotAccessPolicy.IsAllowed(456, allowlist, adminChatId: 777, allowPublicAccess: false), "allowlist includes 456");
        AssertFalse(BotAccessPolicy.IsAllowed(999, allowlist, adminChatId: 777, allowPublicAccess: false), "allowlist blocks unknown chat when configured");
        AssertFalse(BotAccessPolicy.IsAllowed(999, emptyAllowlist, adminChatId: 0, allowPublicAccess: false), "empty allowlist fails closed when public access is not explicitly enabled");
        AssertTrue(BotAccessPolicy.IsAllowed(999, emptyAllowlist, adminChatId: 0, allowPublicAccess: true), "public override allows unknown chat when explicitly enabled");
        AssertTrue(BotAccessPolicy.IsAllowed(777, emptyAllowlist, adminChatId: 777, allowPublicAccess: false), "admin chat is allowed even without allowlist");
        AssertTrue(BotAccessPolicy.AccessDeniedMessage(false, emptyAllowlist, 0).Contains("ALLOW_PUBLIC_ACCESS"), "AccessDeniedMessage explains fully locked configuration");
        AssertTrue(BotAccessPolicy.AccessDeniedMessage(false, allowlist, 0).Contains("administrator", StringComparison.OrdinalIgnoreCase), "AccessDeniedMessage gives normal allowlist denial text");
        AssertEqual("allowlist", BotAccessPolicy.DescribeAccessMode(allowlist, adminChatId: 0, allowPublicAccess: false), "DescribeAccessMode reports allowlist mode");
        AssertEqual("admin-only", BotAccessPolicy.DescribeAccessMode(emptyAllowlist, adminChatId: 777, allowPublicAccess: false), "DescribeAccessMode reports admin-only mode");
        AssertEqual("public override", BotAccessPolicy.DescribeAccessMode(emptyAllowlist, adminChatId: 0, allowPublicAccess: true), "DescribeAccessMode reports public override mode");
        AssertEqual("locked", BotAccessPolicy.DescribeAccessMode(emptyAllowlist, adminChatId: 0, allowPublicAccess: false), "DescribeAccessMode reports locked mode");

        string scriptsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "scripts");
        string localDevEnvironmentScript = Path.Combine(scriptsDirectory, "Set-LocalDevEnvironment.ps1");
        string safeEnvironmentScript = Path.Combine(scriptsDirectory, "Set-SafeEnvironment.ps1");
        AssertTrue(File.Exists(localDevEnvironmentScript), "Set-LocalDevEnvironment.ps1 exists for local machine profile setup");
        AssertTrue(File.Exists(safeEnvironmentScript), "Set-SafeEnvironment.ps1 exists for reverting risky local machine flags");
        string localDevEnvironmentContent = File.ReadAllText(localDevEnvironmentScript);
        string safeEnvironmentContent = File.ReadAllText(safeEnvironmentScript);
        AssertTrue(localDevEnvironmentContent.Contains("User", StringComparison.OrdinalIgnoreCase), "Set-LocalDevEnvironment.ps1 writes User environment variables only");
        AssertTrue(safeEnvironmentContent.Contains("User", StringComparison.OrdinalIgnoreCase), "Set-SafeEnvironment.ps1 writes User environment variables only");
        AssertFalse(localDevEnvironmentContent.Contains("SetEnvironmentVariable('TELEGRAM_BOT_TOKEN'", StringComparison.OrdinalIgnoreCase) || localDevEnvironmentContent.Contains("SetEnvironmentVariable(\"TELEGRAM_BOT_TOKEN\"", StringComparison.OrdinalIgnoreCase), "Set-LocalDevEnvironment.ps1 does not write Telegram token secrets");
        AssertFalse(localDevEnvironmentContent.Contains("SetEnvironmentVariable('GITHUB_TOKEN'", StringComparison.OrdinalIgnoreCase) || localDevEnvironmentContent.Contains("SetEnvironmentVariable(\"GITHUB_TOKEN\"", StringComparison.OrdinalIgnoreCase), "Set-LocalDevEnvironment.ps1 does not write GitHub token secrets");
        AssertTrue(localDevEnvironmentContent.Contains("SEARCH_ROUTING_MODE", StringComparison.OrdinalIgnoreCase) && localDevEnvironmentContent.Contains("llm", StringComparison.OrdinalIgnoreCase), "Set-LocalDevEnvironment.ps1 enables LLM search routing for local dev");
        AssertTrue(safeEnvironmentContent.Contains("ALLOW_PUBLIC_ACCESS", StringComparison.OrdinalIgnoreCase) && safeEnvironmentContent.Contains("false", StringComparison.OrdinalIgnoreCase), "Set-SafeEnvironment.ps1 disables public access");
        AssertTrue(safeEnvironmentContent.Contains("LOG_MESSAGE_CONTENT", StringComparison.OrdinalIgnoreCase) && safeEnvironmentContent.Contains("false", StringComparison.OrdinalIgnoreCase), "Set-SafeEnvironment.ps1 disables content logging");
        AssertTrue(safeEnvironmentContent.Contains("ENABLE_REPO_WRITE_TOOLS", StringComparison.OrdinalIgnoreCase) && safeEnvironmentContent.Contains("false", StringComparison.OrdinalIgnoreCase), "Set-SafeEnvironment.ps1 disables repo write tools");
        AssertTrue(safeEnvironmentContent.Contains("ENABLE_GITHUB_WRITE_TOOLS", StringComparison.OrdinalIgnoreCase) && safeEnvironmentContent.Contains("false", StringComparison.OrdinalIgnoreCase), "Set-SafeEnvironment.ps1 disables GitHub write tools");

        AssertEqual(8, BotConfiguration.ParseClampedInt(null, defaultValue: 8, minValue: 1, maxValue: 50), "ParseClampedInt returns default for missing value");
        AssertEqual(12, BotConfiguration.ParseClampedInt("12", defaultValue: 8, minValue: 1, maxValue: 50), "ParseClampedInt parses valid value");
        AssertEqual(1, BotConfiguration.ParseClampedInt("0", defaultValue: 8, minValue: 1, maxValue: 50), "ParseClampedInt clamps below minimum");
        AssertEqual(50, BotConfiguration.ParseClampedInt("500", defaultValue: 8, minValue: 1, maxValue: 50), "ParseClampedInt clamps above maximum");
        AssertEqual(8, BotConfiguration.ParseClampedInt("not-a-number", defaultValue: 8, minValue: 1, maxValue: 50), "ParseClampedInt returns default for invalid value");
        string? previousConversationMaxHistory = Environment.GetEnvironmentVariable("CONVERSATION_MAX_HISTORY");
        string? previousSearchRoutingMode = Environment.GetEnvironmentVariable("SEARCH_ROUTING_MODE");
        string? previousEnableSafeCommandTools = Environment.GetEnvironmentVariable("ENABLE_SAFE_COMMAND_TOOLS");
        string? previousSafeCommandProjectRoot = Environment.GetEnvironmentVariable("SAFE_COMMAND_PROJECT_ROOT");
        string? previousEnableRepoWriteTools = Environment.GetEnvironmentVariable("ENABLE_REPO_WRITE_TOOLS");
        string? previousEnablePlugins = Environment.GetEnvironmentVariable("ENABLE_PLUGINS");
        string? previousPluginDirectory = Environment.GetEnvironmentVariable("PLUGIN_DIRECTORY");
        string? previousEnableGitHubTools = Environment.GetEnvironmentVariable("ENABLE_GITHUB_TOOLS");
        string? previousEnableGitHubWriteTools = Environment.GetEnvironmentVariable("ENABLE_GITHUB_WRITE_TOOLS");
        string? previousImageDescriptionPrompt = Environment.GetEnvironmentVariable("IMAGE_DESCRIPTION_PROMPT");
        string? previousEnableImageOcr = Environment.GetEnvironmentVariable("ENABLE_IMAGE_OCR");
        string? previousImageOcrCommand = Environment.GetEnvironmentVariable("IMAGE_OCR_COMMAND");
        string? previousImageOcrArguments = Environment.GetEnvironmentVariable("IMAGE_OCR_ARGUMENTS");
        string? previousImageOcrTimeoutSeconds = Environment.GetEnvironmentVariable("IMAGE_OCR_TIMEOUT_SECONDS");
        string? previousAudioTranscriptionCommand = Environment.GetEnvironmentVariable("AUDIO_TRANSCRIPTION_COMMAND");
        string? previousAudioTranscriptionArguments = Environment.GetEnvironmentVariable("AUDIO_TRANSCRIPTION_ARGUMENTS");
        string? previousAudioTranscriptionTimeoutSeconds = Environment.GetEnvironmentVariable("AUDIO_TRANSCRIPTION_TIMEOUT_SECONDS");
        string? previousEnableTextToSpeech = Environment.GetEnvironmentVariable("ENABLE_TEXT_TO_SPEECH");
        string? previousTextToSpeechCommand = Environment.GetEnvironmentVariable("TEXT_TO_SPEECH_COMMAND");
        string? previousTextToSpeechArguments = Environment.GetEnvironmentVariable("TEXT_TO_SPEECH_ARGUMENTS");
        string? previousTextToSpeechTimeoutSeconds = Environment.GetEnvironmentVariable("TEXT_TO_SPEECH_TIMEOUT_SECONDS");
        string? previousTextToSpeechOutputExtension = Environment.GetEnvironmentVariable("TEXT_TO_SPEECH_OUTPUT_EXTENSION");
        string? previousGitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        string? previousGitHubDefaultOwner = Environment.GetEnvironmentVariable("GITHUB_DEFAULT_OWNER");
        string? previousGitHubDefaultRepo = Environment.GetEnvironmentVariable("GITHUB_DEFAULT_REPO");
        string? previousGitHubAllowedRepos = Environment.GetEnvironmentVariable("GITHUB_ALLOWED_REPOS");
        try
        {
            Environment.SetEnvironmentVariable("CONVERSATION_MAX_HISTORY", "13");
            AssertEqual(13, BotConfiguration.LoadFromEnvironment().ConversationMaxHistory, "LoadFromEnvironment reads CONVERSATION_MAX_HISTORY");

            Environment.SetEnvironmentVariable("CONVERSATION_MAX_HISTORY", "500");
            AssertEqual(50, BotConfiguration.LoadFromEnvironment().ConversationMaxHistory, "LoadFromEnvironment clamps high CONVERSATION_MAX_HISTORY");

            Environment.SetEnvironmentVariable("SEARCH_ROUTING_MODE", null);
            AssertEqual("heuristic", BotConfiguration.LoadFromEnvironment().SearchRoutingMode, "LoadFromEnvironment defaults SEARCH_ROUTING_MODE to heuristic");

            Environment.SetEnvironmentVariable("SEARCH_ROUTING_MODE", "off");
            AssertEqual("off", BotConfiguration.LoadFromEnvironment().SearchRoutingMode, "LoadFromEnvironment reads SEARCH_ROUTING_MODE=off");

            Environment.SetEnvironmentVariable("SEARCH_ROUTING_MODE", "llm");
            AssertEqual("llm", BotConfiguration.LoadFromEnvironment().SearchRoutingMode, "LoadFromEnvironment reads SEARCH_ROUTING_MODE=llm");

            Environment.SetEnvironmentVariable("SEARCH_ROUTING_MODE", "unknown");
            AssertEqual("heuristic", BotConfiguration.LoadFromEnvironment().SearchRoutingMode, "LoadFromEnvironment falls back to heuristic for invalid SEARCH_ROUTING_MODE");

            Environment.SetEnvironmentVariable("ENABLE_SAFE_COMMAND_TOOLS", null);
            AssertFalse(BotConfiguration.LoadFromEnvironment().EnableSafeCommandTools, "LoadFromEnvironment defaults safe command tools to disabled");

            Environment.SetEnvironmentVariable("ENABLE_SAFE_COMMAND_TOOLS", "true");
            Environment.SetEnvironmentVariable("SAFE_COMMAND_PROJECT_ROOT", Directory.GetCurrentDirectory());
            BotSettings safeCommandEnvironmentSettings = BotConfiguration.LoadFromEnvironment();
            AssertTrue(safeCommandEnvironmentSettings.EnableSafeCommandTools, "LoadFromEnvironment parses ENABLE_SAFE_COMMAND_TOOLS truthy values");
            AssertEqual(Path.GetFullPath(Directory.GetCurrentDirectory()), safeCommandEnvironmentSettings.SafeCommandProjectRoot, "LoadFromEnvironment reads SAFE_COMMAND_PROJECT_ROOT as a full path");

            Environment.SetEnvironmentVariable("ENABLE_REPO_WRITE_TOOLS", null);
            AssertFalse(BotConfiguration.LoadFromEnvironment().EnableRepoWriteTools, "LoadFromEnvironment defaults repo write tools to disabled");

            Environment.SetEnvironmentVariable("ENABLE_REPO_WRITE_TOOLS", "yes");
            AssertTrue(BotConfiguration.LoadFromEnvironment().EnableRepoWriteTools, "LoadFromEnvironment parses ENABLE_REPO_WRITE_TOOLS truthy values");

            Environment.SetEnvironmentVariable("IMAGE_DESCRIPTION_PROMPT", null);
            AssertEqual(BotConfiguration.DefaultImageDescriptionPrompt, BotConfiguration.LoadFromEnvironment().ImageDescriptionPrompt, "LoadFromEnvironment defaults IMAGE_DESCRIPTION_PROMPT safely");

            Environment.SetEnvironmentVariable("IMAGE_DESCRIPTION_PROMPT", "  Focus on UI labels and visible text.  ");
            AssertEqual("Focus on UI labels and visible text.", BotConfiguration.LoadFromEnvironment().ImageDescriptionPrompt, "LoadFromEnvironment trims IMAGE_DESCRIPTION_PROMPT");

            Environment.SetEnvironmentVariable("ENABLE_IMAGE_OCR", null);
            Environment.SetEnvironmentVariable("IMAGE_OCR_COMMAND", null);
            Environment.SetEnvironmentVariable("IMAGE_OCR_ARGUMENTS", null);
            Environment.SetEnvironmentVariable("IMAGE_OCR_TIMEOUT_SECONDS", null);
            BotSettings defaultOcrProviderSettings = BotConfiguration.LoadFromEnvironment();
            AssertFalse(defaultOcrProviderSettings.EnableImageOcr, "LoadFromEnvironment defaults image OCR to disabled");
            AssertEqual(string.Empty, defaultOcrProviderSettings.ImageOcrCommand, "LoadFromEnvironment defaults IMAGE_OCR_COMMAND to empty");
            AssertEqual("{file}", defaultOcrProviderSettings.ImageOcrArguments, "LoadFromEnvironment defaults IMAGE_OCR_ARGUMENTS to file placeholder");
            AssertEqual(120, defaultOcrProviderSettings.ImageOcrTimeoutSeconds, "LoadFromEnvironment defaults IMAGE_OCR_TIMEOUT_SECONDS safely");

            Environment.SetEnvironmentVariable("ENABLE_IMAGE_OCR", "yes");
            Environment.SetEnvironmentVariable("IMAGE_OCR_COMMAND", "  tesseract-cli  ");
            Environment.SetEnvironmentVariable("IMAGE_OCR_ARGUMENTS", " --image \"{file}\" ");
            Environment.SetEnvironmentVariable("IMAGE_OCR_TIMEOUT_SECONDS", "700");
            BotSettings ocrProviderSettings = BotConfiguration.LoadFromEnvironment();
            AssertTrue(ocrProviderSettings.EnableImageOcr, "LoadFromEnvironment parses ENABLE_IMAGE_OCR truthy values");
            AssertEqual("tesseract-cli", ocrProviderSettings.ImageOcrCommand, "LoadFromEnvironment trims IMAGE_OCR_COMMAND");
            AssertEqual("--image \"{file}\"", ocrProviderSettings.ImageOcrArguments, "LoadFromEnvironment trims IMAGE_OCR_ARGUMENTS");
            AssertEqual(300, ocrProviderSettings.ImageOcrTimeoutSeconds, "LoadFromEnvironment clamps IMAGE_OCR_TIMEOUT_SECONDS high");

            Environment.SetEnvironmentVariable("AUDIO_TRANSCRIPTION_COMMAND", null);
            Environment.SetEnvironmentVariable("AUDIO_TRANSCRIPTION_ARGUMENTS", null);
            Environment.SetEnvironmentVariable("AUDIO_TRANSCRIPTION_TIMEOUT_SECONDS", null);
            BotSettings defaultAudioProviderSettings = BotConfiguration.LoadFromEnvironment();
            AssertEqual(string.Empty, defaultAudioProviderSettings.AudioTranscriptionCommand, "LoadFromEnvironment defaults AUDIO_TRANSCRIPTION_COMMAND to empty");
            AssertEqual("{file}", defaultAudioProviderSettings.AudioTranscriptionArguments, "LoadFromEnvironment defaults AUDIO_TRANSCRIPTION_ARGUMENTS to file placeholder");
            AssertEqual(120, defaultAudioProviderSettings.AudioTranscriptionTimeoutSeconds, "LoadFromEnvironment defaults AUDIO_TRANSCRIPTION_TIMEOUT_SECONDS safely");

            Environment.SetEnvironmentVariable("AUDIO_TRANSCRIPTION_COMMAND", "  whisper-cli  ");
            Environment.SetEnvironmentVariable("AUDIO_TRANSCRIPTION_ARGUMENTS", " --model base.en --file \"{file}\" ");
            Environment.SetEnvironmentVariable("AUDIO_TRANSCRIPTION_TIMEOUT_SECONDS", "500");
            BotSettings audioProviderSettings = BotConfiguration.LoadFromEnvironment();
            AssertEqual("whisper-cli", audioProviderSettings.AudioTranscriptionCommand, "LoadFromEnvironment trims AUDIO_TRANSCRIPTION_COMMAND");
            AssertEqual("--model base.en --file \"{file}\"", audioProviderSettings.AudioTranscriptionArguments, "LoadFromEnvironment trims AUDIO_TRANSCRIPTION_ARGUMENTS");
            AssertEqual(300, audioProviderSettings.AudioTranscriptionTimeoutSeconds, "LoadFromEnvironment clamps AUDIO_TRANSCRIPTION_TIMEOUT_SECONDS high");

            Environment.SetEnvironmentVariable("ENABLE_TEXT_TO_SPEECH", null);
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_COMMAND", null);
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_ARGUMENTS", null);
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_TIMEOUT_SECONDS", null);
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_OUTPUT_EXTENSION", null);
            BotSettings defaultTextToSpeechSettings = BotConfiguration.LoadFromEnvironment();
            AssertFalse(defaultTextToSpeechSettings.EnableTextToSpeech, "LoadFromEnvironment defaults text-to-speech to disabled");
            AssertEqual(string.Empty, defaultTextToSpeechSettings.TextToSpeechCommand, "LoadFromEnvironment defaults TEXT_TO_SPEECH_COMMAND to empty");
            AssertEqual("{text} {output}", defaultTextToSpeechSettings.TextToSpeechArguments, "LoadFromEnvironment defaults TEXT_TO_SPEECH_ARGUMENTS safely");
            AssertEqual(120, defaultTextToSpeechSettings.TextToSpeechTimeoutSeconds, "LoadFromEnvironment defaults TEXT_TO_SPEECH_TIMEOUT_SECONDS safely");
            AssertEqual(".mp3", defaultTextToSpeechSettings.TextToSpeechOutputExtension, "LoadFromEnvironment defaults TEXT_TO_SPEECH_OUTPUT_EXTENSION safely");

            Environment.SetEnvironmentVariable("ENABLE_TEXT_TO_SPEECH", "yes");
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_COMMAND", "  tts-cli  ");
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_ARGUMENTS", " --text \"{text}\" --out \"{output}\" ");
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_TIMEOUT_SECONDS", "1");
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_OUTPUT_EXTENSION", " wav ");
            BotSettings textToSpeechSettings = BotConfiguration.LoadFromEnvironment();
            AssertTrue(textToSpeechSettings.EnableTextToSpeech, "LoadFromEnvironment parses ENABLE_TEXT_TO_SPEECH truthy values");
            AssertEqual("tts-cli", textToSpeechSettings.TextToSpeechCommand, "LoadFromEnvironment trims TEXT_TO_SPEECH_COMMAND");
            AssertEqual("--text \"{text}\" --out \"{output}\"", textToSpeechSettings.TextToSpeechArguments, "LoadFromEnvironment trims TEXT_TO_SPEECH_ARGUMENTS");
            AssertEqual(5, textToSpeechSettings.TextToSpeechTimeoutSeconds, "LoadFromEnvironment clamps TEXT_TO_SPEECH_TIMEOUT_SECONDS low");
            AssertEqual(".wav", textToSpeechSettings.TextToSpeechOutputExtension, "LoadFromEnvironment normalizes TEXT_TO_SPEECH_OUTPUT_EXTENSION");

            Environment.SetEnvironmentVariable("ENABLE_PLUGINS", null);
            AssertFalse(BotConfiguration.LoadFromEnvironment().EnablePlugins, "LoadFromEnvironment defaults plugins to disabled");

            string pluginDirectory = Path.Combine(Directory.GetCurrentDirectory(), "custom-plugins");
            Environment.SetEnvironmentVariable("ENABLE_PLUGINS", "yes");
            Environment.SetEnvironmentVariable("PLUGIN_DIRECTORY", pluginDirectory);
            BotSettings pluginEnvironmentSettings = BotConfiguration.LoadFromEnvironment();
            AssertTrue(pluginEnvironmentSettings.EnablePlugins, "LoadFromEnvironment parses ENABLE_PLUGINS truthy values");
            AssertEqual(Path.GetFullPath(pluginDirectory), pluginEnvironmentSettings.PluginDirectory, "LoadFromEnvironment reads PLUGIN_DIRECTORY as a full path");


            Environment.SetEnvironmentVariable("ENABLE_GITHUB_TOOLS", null);
            Environment.SetEnvironmentVariable("ENABLE_GITHUB_WRITE_TOOLS", null);
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
            Environment.SetEnvironmentVariable("GITHUB_DEFAULT_OWNER", null);
            Environment.SetEnvironmentVariable("GITHUB_DEFAULT_REPO", null);
            Environment.SetEnvironmentVariable("GITHUB_ALLOWED_REPOS", null);
            AssertFalse(BotConfiguration.LoadFromEnvironment().GitHub.EnableGitHubTools, "LoadFromEnvironment defaults GitHub tools to disabled");
            AssertFalse(BotConfiguration.LoadFromEnvironment().GitHub.EnableGitHubWriteTools, "LoadFromEnvironment defaults GitHub write tools to disabled");

            Environment.SetEnvironmentVariable("ENABLE_GITHUB_TOOLS", "true");
            Environment.SetEnvironmentVariable("ENABLE_GITHUB_WRITE_TOOLS", "yes");
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "secret-token-for-test");
            Environment.SetEnvironmentVariable("GITHUB_DEFAULT_OWNER", "mujahedgt");
            Environment.SetEnvironmentVariable("GITHUB_DEFAULT_REPO", "TelegramMessagingTool");
            Environment.SetEnvironmentVariable("GITHUB_ALLOWED_REPOS", "mujahedgt/TelegramMessagingTool, mujahedgt/IsolationForestServer");
            BotSettings gitHubEnvironmentSettings = BotConfiguration.LoadFromEnvironment();
            AssertTrue(gitHubEnvironmentSettings.GitHub.EnableGitHubTools, "LoadFromEnvironment parses ENABLE_GITHUB_TOOLS truthy values");
            AssertTrue(gitHubEnvironmentSettings.GitHub.EnableGitHubWriteTools, "LoadFromEnvironment parses ENABLE_GITHUB_WRITE_TOOLS truthy values");
            AssertEqual("mujahedgt/TelegramMessagingTool", gitHubEnvironmentSettings.GitHub.DefaultFullName, "LoadFromEnvironment reads GitHub default repo");
            AssertTrue(gitHubEnvironmentSettings.GitHub.AllowedRepos.Contains("mujahedgt/IsolationForestServer"), "LoadFromEnvironment reads GitHub allowed repos");
            AssertFalse(gitHubEnvironmentSettings.GitHub.RenderSafeSummary().Contains("secret-token-for-test"), "GitHub settings safe summary does not expose token value");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONVERSATION_MAX_HISTORY", previousConversationMaxHistory);
            Environment.SetEnvironmentVariable("SEARCH_ROUTING_MODE", previousSearchRoutingMode);
            Environment.SetEnvironmentVariable("ENABLE_SAFE_COMMAND_TOOLS", previousEnableSafeCommandTools);
            Environment.SetEnvironmentVariable("SAFE_COMMAND_PROJECT_ROOT", previousSafeCommandProjectRoot);
            Environment.SetEnvironmentVariable("ENABLE_REPO_WRITE_TOOLS", previousEnableRepoWriteTools);
            Environment.SetEnvironmentVariable("ENABLE_PLUGINS", previousEnablePlugins);
            Environment.SetEnvironmentVariable("PLUGIN_DIRECTORY", previousPluginDirectory);
            Environment.SetEnvironmentVariable("ENABLE_GITHUB_TOOLS", previousEnableGitHubTools);
            Environment.SetEnvironmentVariable("ENABLE_GITHUB_WRITE_TOOLS", previousEnableGitHubWriteTools);
            Environment.SetEnvironmentVariable("IMAGE_DESCRIPTION_PROMPT", previousImageDescriptionPrompt);
            Environment.SetEnvironmentVariable("ENABLE_IMAGE_OCR", previousEnableImageOcr);
            Environment.SetEnvironmentVariable("IMAGE_OCR_COMMAND", previousImageOcrCommand);
            Environment.SetEnvironmentVariable("IMAGE_OCR_ARGUMENTS", previousImageOcrArguments);
            Environment.SetEnvironmentVariable("IMAGE_OCR_TIMEOUT_SECONDS", previousImageOcrTimeoutSeconds);
            Environment.SetEnvironmentVariable("AUDIO_TRANSCRIPTION_COMMAND", previousAudioTranscriptionCommand);
            Environment.SetEnvironmentVariable("AUDIO_TRANSCRIPTION_ARGUMENTS", previousAudioTranscriptionArguments);
            Environment.SetEnvironmentVariable("AUDIO_TRANSCRIPTION_TIMEOUT_SECONDS", previousAudioTranscriptionTimeoutSeconds);
            Environment.SetEnvironmentVariable("ENABLE_TEXT_TO_SPEECH", previousEnableTextToSpeech);
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_COMMAND", previousTextToSpeechCommand);
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_ARGUMENTS", previousTextToSpeechArguments);
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_TIMEOUT_SECONDS", previousTextToSpeechTimeoutSeconds);
            Environment.SetEnvironmentVariable("TEXT_TO_SPEECH_OUTPUT_EXTENSION", previousTextToSpeechOutputExtension);
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previousGitHubToken);
            Environment.SetEnvironmentVariable("GITHUB_DEFAULT_OWNER", previousGitHubDefaultOwner);
            Environment.SetEnvironmentVariable("GITHUB_DEFAULT_REPO", previousGitHubDefaultRepo);
            Environment.SetEnvironmentVariable("GITHUB_ALLOWED_REPOS", previousGitHubAllowedRepos);
        }
    }
}
