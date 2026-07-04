using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using TelegramMessagingTool;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.models;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Runtime;
using TelegramMessagingTool.Telegram;

BotSettings settings = BotConfiguration.LoadFromEnvironment();

if (string.IsNullOrWhiteSpace(settings.BotToken))
{
    Console.WriteLine("Missing TELEGRAM_BOT_TOKEN environment variable.");
    Console.WriteLine("Set it first, then restart the app.");
    return;
}

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

using AppServices appServices = AppServicesBuilder.Build(settings, ConsoleEventWriter.Write, () => cts.Cancel());

if (settings.ApplyMigrations)
{
    await using var startupDb = new TelegramDbContext();
    await startupDb.Database.MigrateAsync(cts.Token);
}

ReceiverOptions receiverOptions = new()
{
    AllowedUpdates = Array.Empty<UpdateType>()
};

try
{
    var me = await appServices.BotClient.GetMe(cts.Token);
    Console.WriteLine(AgentConsoleRenderer.RenderStartupPanel(new AgentConsoleSnapshot(
        BotUsername: me.Username ?? "unknown",
        OllamaUrl: settings.OllamaUrl,
        OllamaModel: settings.OllamaModel,
        DatabaseConnection: settings.DatabaseConnectionString,
        AccessMode: BotAccessPolicy.DescribeAccessMode(settings.AllowedChatIds, settings.AdminChatId, settings.AllowPublicAccess),
        MessageContentLoggingEnabled: settings.LogMessageContent,
        OnlineSearchEnabled: settings.EnableOnlineSearch,
        ApplyMigrations: settings.ApplyMigrations,
        Commands: appServices.CommandRouter.Commands.Select(x => x.Name).ToList(),
        Tools: appServices.ToolRegistry.Tools.Select(x => x.Name).ToList())));

    appServices.BotClient.StartReceiving(
        updateHandler: appServices.TelegramUpdateHandler.HandleUpdateAsync,
        errorHandler: appServices.TelegramUpdateHandler.HandleErrorAsync,
        receiverOptions: receiverOptions,
        cancellationToken: cts.Token
    );

    ConsoleEventWriter.Write("START", me.Username ?? "bot", "long polling is running", ConsoleEventLevel.Success);
    ConsoleEventWriter.Write("CONSOLE", "local", "type a message or command here; use /exit to stop", ConsoleEventLevel.Info);

    _ = appServices.ConsoleInputHandler.RunAsync(cts.Token);
    _ = appServices.TaskReminderLoop.RunAsync(cts.Token);

    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
    }
    catch (OperationCanceledException)
    {
        // Graceful shutdown requested from console, Ctrl+C, or host termination.
    }

    ConsoleEventWriter.Write("STOP", "system", "shutdown requested", ConsoleEventLevel.Warning);
    cts.Cancel();
}
catch (ApiRequestException apiEx)
{
    SystemLogging.Instance.Log(
        0,
        "System",
        "Telegram API error occurred",
        $"[{apiEx.ErrorCode}] {apiEx.Message}",
        LogType.Error);

    Console.WriteLine($"Telegram API Error: [{apiEx.ErrorCode}] {apiEx.Message}");
}
catch (Exception ex)
{
    SystemLogging.Instance.Log(
        0,
        "System",
        "Unexpected error occurred",
        ex.Message,
        LogType.Error);

    Console.WriteLine($"Unexpected Error: {ex.Message}");
}
