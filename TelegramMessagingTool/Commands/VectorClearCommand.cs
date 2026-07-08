using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services.Vector;

namespace TelegramMessagingTool.Commands;

public sealed class VectorClearCommand : IBotCommand
{
    private readonly VectorMaintenanceService _maintenanceService;

    public VectorClearCommand(VectorMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
    }

    public string Name => "/vectorclear";

    public string Description => "Clear current user's mirrored vectors from the configured vector store.";

    public async Task<CommandResult> TryHandleAsync(Message message, ConnectedUser user, TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        VectorMaintenanceResult result = await _maintenanceService.ClearAsync(dbContext, user, cancellationToken);
        return new CommandResult(true, result.Message, ReactionEmoji: "🧹");
    }
}
