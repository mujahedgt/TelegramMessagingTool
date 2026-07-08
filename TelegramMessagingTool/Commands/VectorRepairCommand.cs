using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services.Vector;

namespace TelegramMessagingTool.Commands;

public sealed class VectorRepairCommand : IBotCommand
{
    private readonly VectorMaintenanceService _maintenanceService;

    public VectorRepairCommand(VectorMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
    }

    public string Name => "/vectorrepair";

    public string Description => "Repair document vector embeddings by re-indexing when needed and re-syncing vectors.";

    public async Task<CommandResult> TryHandleAsync(Message message, ConnectedUser user, TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        try
        {
            VectorMaintenanceResult result = await _maintenanceService.RepairAsync(dbContext, user, cancellationToken);
            return new CommandResult(true, result.Message, ReactionEmoji: "🛠");
        }
        catch (InvalidOperationException ex)
        {
            return new CommandResult(true, ex.Message);
        }
    }
}
