using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;
using TelegramMessagingTool.Services.Vector;

namespace TelegramMessagingTool.Commands;

public sealed class VectorSyncCommand : IBotCommand
{
    private readonly VectorMaintenanceService _maintenanceService;

    public VectorSyncCommand(VectorMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
    }

    public string Name => "/vectorsync";

    public string Description => "Sync indexed document embeddings into the configured vector store.";

    public async Task<CommandResult> TryHandleAsync(Message message, ConnectedUser user, TelegramDbContext dbContext, CancellationToken cancellationToken)
    {
        string messageText = message.Text ?? string.Empty;
        if (!CommandParser.Matches(messageText, Name))
        {
            return new CommandResult(false, null);
        }

        try
        {
            VectorMaintenanceResult result = await _maintenanceService.SyncAsync(dbContext, user, cancellationToken);
            return new CommandResult(true, result.Message, ReactionEmoji: "✅");
        }
        catch (InvalidOperationException ex)
        {
            return new CommandResult(true, ex.Message);
        }
    }
}
