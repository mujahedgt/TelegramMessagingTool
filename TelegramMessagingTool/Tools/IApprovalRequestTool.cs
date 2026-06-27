using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Tools;

public interface IApprovalRequestTool : IAgentTool
{
    Task<ToolResult> CreatePendingActionAsync(
        string input,
        TelegramDbContext dbContext,
        ConnectedUser user,
        CancellationToken cancellationToken);
}
