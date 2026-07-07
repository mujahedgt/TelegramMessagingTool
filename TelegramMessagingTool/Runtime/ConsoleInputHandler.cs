using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Agent;
using TelegramMessagingTool.Commands;
using TelegramMessagingTool.ConsoleUi;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.models;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services;
using TelegramMessagingTool.Tools;

namespace TelegramMessagingTool.Runtime;

public sealed class ConsoleInputHandler
{
    private const long ConsoleChatId = 0;

    private readonly BotSettings _settings;
    private readonly ToolRegistry _toolRegistry;
    private readonly AgentRunner _agentRunner;
    private readonly ConversationService _conversationService;
    private readonly CommandRouter _commandRouter;
    private readonly RuntimeDashboardService _runtimeDashboardService;
    private readonly Action<string, string, string, ConsoleEventLevel> _writeConsoleEvent;
    private readonly Action _requestShutdown;

    public ConsoleInputHandler(
        BotSettings settings,
        ToolRegistry toolRegistry,
        AgentRunner agentRunner,
        ConversationService conversationService,
        CommandRouter commandRouter,
        RuntimeDashboardService runtimeDashboardService,
        Action<string, string, string, ConsoleEventLevel> writeConsoleEvent,
        Action requestShutdown)
    {
        _settings = settings;
        _toolRegistry = toolRegistry;
        _agentRunner = agentRunner;
        _conversationService = conversationService;
        _commandRouter = commandRouter;
        _runtimeDashboardService = runtimeDashboardService;
        _writeConsoleEvent = writeConsoleEvent;
        _requestShutdown = requestShutdown;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("> ");
            string? line = await Task.Run(Console.ReadLine, cancellationToken);
            if (line is null)
            {
                // In Windows Startup/background launchers stdin can be closed.
                // Keep Telegram long polling alive instead of shutting down the bot.
                _writeConsoleEvent("CONSOLE", "local", "stdin is closed; Telegram bot continues without console input", ConsoleEventLevel.Warning);
                return;
            }

            string input = line.Trim();
            if (input.Length == 0)
            {
                continue;
            }

            if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase)
                || input.Equals("exit", StringComparison.OrdinalIgnoreCase)
                || input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                _requestShutdown();
                return;
            }

            try
            {
                string answer = await ProcessInputAsync(input, cancellationToken);
                Console.WriteLine();
                Console.WriteLine(answer);
                Console.WriteLine();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _writeConsoleEvent("ERROR", "console", ex.Message, ConsoleEventLevel.Error);
            }
        }
    }

    public async Task<string> ProcessInputAsync(string input, CancellationToken cancellationToken)
    {
        await using TelegramDbContext dbContext = new();
        if (input.Equals("/dashboard", StringComparison.OrdinalIgnoreCase)
            || input.Equals("dashboard", StringComparison.OrdinalIgnoreCase))
        {
            _writeConsoleEvent("COMMAND", "console", "/dashboard", ConsoleEventLevel.Success);
            return await _runtimeDashboardService.RenderAsync(dbContext, cancellationToken);
        }

        ConnectedUser consoleUser = await GetOrCreateConsoleUserAsync(dbContext, cancellationToken);

        var consoleMessage = new Message
        {
            Text = input,
            Chat = new Chat
            {
                Id = consoleUser.ChatId,
                Username = "local_console",
                FirstName = "Local",
                LastName = "Console"
            }
        };

        CommandResult commandResult = await _commandRouter.TryHandleAsync(consoleMessage, consoleUser, dbContext, cancellationToken);
        if (commandResult.Handled)
        {
            _writeConsoleEvent("COMMAND", "console", input.Split(' ', 2)[0], ConsoleEventLevel.Success);
            return commandResult.ReplyText ?? "Command completed.";
        }

        dbContext.Messages.Add(new ChatMessage
        {
            ConnectedUserId = consoleUser.Id,
            ChatId = consoleUser.ChatId,
            Content = input,
            Role = ChatRoles.User,
            Timestamp = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        List<OllamaMessageDto> conversationContext = await _conversationService.CreateConversationContextAsync(
            dbContext,
            consoleUser.Id,
            maxHistory: _settings.ConversationMaxHistory,
            cancellationToken: cancellationToken,
            toolInstructions: _toolRegistry.RenderToolInstructions());

        string finalAnswer = await _agentRunner.RunAsync(
            conversationContext,
            cancellationToken,
            dbContext,
            consoleUser);
        _writeConsoleEvent("MESSAGE", "console", $"answered {finalAnswer.Length} chars", ConsoleEventLevel.Success);

        dbContext.Messages.Add(new ChatMessage
        {
            ConnectedUserId = consoleUser.Id,
            ChatId = consoleUser.ChatId,
            Content = finalAnswer,
            Role = ChatRoles.Assistant,
            Timestamp = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return finalAnswer;
    }

    private static async Task<ConnectedUser> GetOrCreateConsoleUserAsync(TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        ConnectedUser? user = await dbContext.Users.FirstOrDefaultAsync(x => x.ChatId == ConsoleChatId, cancellationToken);
        if (user is not null)
        {
            user.Name = "local_console";
            user.FirstName = "Local";
            user.LastName = "Console";
            user.LastSeenAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return user;
        }

        user = new ConnectedUser
        {
            ChatId = ConsoleChatId,
            Name = "local_console",
            FirstName = "Local",
            LastName = "Console",
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }
}
