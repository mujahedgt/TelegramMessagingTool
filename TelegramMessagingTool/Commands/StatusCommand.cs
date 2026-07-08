using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;

namespace TelegramMessagingTool.Commands;

public sealed class StatusCommand : IBotCommand
{
    private readonly BotSettings _settings;

    public StatusCommand(BotSettings settings)
    {
        _settings = settings;
    }

    public string Name => "/status";
    public string Description => "Show bot runtime status.";

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

        bool dbReady = await dbContext.Database.CanConnectAsync(cancellationToken);
        var modelRoutingService = new ModelRoutingService(_settings);

        string reply = $"""
Status

Database: {(dbReady ? "OK" : "Unavailable")}
Ollama URL: {_settings.OllamaUrl}
Ollama model: {_settings.OllamaModel}
Model routes: {modelRoutingService.RenderSummary()}
Access mode: {BotAccessPolicy.DescribeAccessMode(_settings.AllowedChatIds, _settings.AdminChatId, _settings.AllowPublicAccess)}
Document embeddings: {(_settings.EnableDocumentEmbeddings ? "enabled" : "disabled")}
Online search: {(_settings.EnableOnlineSearch ? "enabled" : "disabled")}
Search routing: {_settings.SearchRoutingMode}
Image vision: {(_settings.EnableImageVision ? "enabled" : "disabled")}
Image prompt: {(_settings.ImageDescriptionPrompt == BotConfiguration.DefaultImageDescriptionPrompt ? "default" : "custom")}
Audio transcription: {(_settings.EnableAudioTranscription ? "enabled" : "disabled")}
Audio provider: {(string.IsNullOrWhiteSpace(_settings.AudioTranscriptionCommand) ? "not configured" : "local command configured")}
Text-to-speech: {(_settings.EnableTextToSpeech ? "enabled" : "disabled")}
TTS provider: {(string.IsNullOrWhiteSpace(_settings.TextToSpeechCommand) ? "not configured" : "local command configured")}
TTS output: {_settings.TextToSpeechOutputExtension}
Telegram typing indicator: {(_settings.EnableTelegramTypingIndicator ? "enabled" : "disabled")}
Safe command tools: {(_settings.EnableSafeCommandTools ? "enabled" : "disabled")}
Repo write tools: {(_settings.EnableRepoWriteTools ? "enabled" : "disabled")}
Plugins: {(_settings.EnablePlugins ? "enabled" : "disabled")} ({_settings.PluginDirectory})
{_settings.GitHub.RenderSafeSummary()}
Message content logging: {(_settings.LogMessageContent ? "enabled" : "disabled")}
Operational observability: tool/action metadata only; message content redacted unless logging is explicitly enabled
Apply migrations: {_settings.ApplyMigrations}
""";

        return new CommandResult(true, reply);
    }
}
