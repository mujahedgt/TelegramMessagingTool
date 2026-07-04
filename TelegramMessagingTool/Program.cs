using Microsoft.EntityFrameworkCore;
using System.Net;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramMessagingTool;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Commands;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.models;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Runtime;
using TelegramMessagingTool.Telegram;
using TelegramMessagingTool.Tools;

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

using AppServices appServices = AppServicesBuilder.Build(settings, WriteConsoleEvent, () => cts.Cancel());

var botClient = appServices.BotClient;
var taskReminderService = appServices.TaskReminderService;
var toolRegistry = appServices.ToolRegistry;
var commandRouter = appServices.CommandRouter;
var telegramUpdateHandler = appServices.TelegramUpdateHandler;
var consoleInputHandler = appServices.ConsoleInputHandler;

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
    var me = await botClient.GetMe(cts.Token);
    Console.WriteLine(AgentConsoleRenderer.RenderStartupPanel(new AgentConsoleSnapshot(
        BotUsername: me.Username ?? "unknown",
        OllamaUrl: settings.OllamaUrl,
        OllamaModel: settings.OllamaModel,
        DatabaseConnection: settings.DatabaseConnectionString,
        AccessMode: BotAccessPolicy.DescribeAccessMode(settings.AllowedChatIds, settings.AdminChatId, settings.AllowPublicAccess),
        MessageContentLoggingEnabled: settings.LogMessageContent,
        OnlineSearchEnabled: settings.EnableOnlineSearch,
        ApplyMigrations: settings.ApplyMigrations,
        Commands: commandRouter.Commands.Select(x => x.Name).ToList(),
        Tools: toolRegistry.Tools.Select(x => x.Name).ToList())));

    botClient.StartReceiving(
        updateHandler: telegramUpdateHandler.HandleUpdateAsync,
        errorHandler: telegramUpdateHandler.HandleErrorAsync,
        receiverOptions: receiverOptions,
        cancellationToken: cts.Token
    );

    WriteConsoleEvent("START", me.Username ?? "bot", "long polling is running", ConsoleEventLevel.Success);
    WriteConsoleEvent("CONSOLE", "local", "type a message or command here; use /exit to stop", ConsoleEventLevel.Info);

    _ = consoleInputHandler.RunAsync(cts.Token);
    _ = RunTaskReminderLoopAsync(cts.Token);
    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
    }
    catch (OperationCanceledException)
    {
        // Graceful shutdown requested from console, Ctrl+C, or host termination.
    }

    WriteConsoleEvent("STOP", "system", "shutdown requested", ConsoleEventLevel.Warning);
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

async Task RunTaskReminderLoopAsync(CancellationToken cancellationToken)
{
    TimeSpan interval = TimeSpan.FromSeconds(60);
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await using TelegramDbContext dbContext = new();
            ReminderScanResult result = await taskReminderService.SendDueRemindersAsync(dbContext, cancellationToken);
            if (result.SentCount > 0 || result.FailedCount > 0)
            {
                WriteConsoleEvent(
                    "REMINDER",
                    "tasks",
                    $"due={result.DueCount} sent={result.SentCount} failed={result.FailedCount}",
                    result.FailedCount > 0 ? ConsoleEventLevel.Warning : ConsoleEventLevel.Success);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            WriteConsoleEvent("REMINDER", "tasks", ex.Message, ConsoleEventLevel.Error);
        }

        try
        {
            await Task.Delay(interval, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }
}

void WriteConsoleEvent(string label, string actor, string detail, ConsoleEventLevel level)
{
    ConsoleColor originalColor = Console.ForegroundColor;
    Console.ForegroundColor = level switch
    {
        ConsoleEventLevel.Success => ConsoleColor.Green,
        ConsoleEventLevel.Warning => ConsoleColor.Yellow,
        ConsoleEventLevel.Error => ConsoleColor.Red,
        _ => ConsoleColor.Cyan
    };

    Console.WriteLine(AgentConsoleRenderer.RenderEvent(label, actor, detail, level));
    Console.ForegroundColor = originalColor;
}
