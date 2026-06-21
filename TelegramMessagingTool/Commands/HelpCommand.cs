using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class HelpCommand : IBotCommand
{
    public string Name => "/help";
    public string Description => "Show available commands.";

    public Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!message.Text?.StartsWith("/help", StringComparison.OrdinalIgnoreCase) ?? true)
        {
            return Task.FromResult(new CommandResult(false, null));
        }

        string reply = """
TelegramMessagingTool Agent Commands

/help - Show this help
/status - Show bot status
/reset - Clear your conversation history
/memory - Show saved memories
/remember <text> - Save a memory
/forget <id> - Delete a memory
/files - List saved files and uploaded documents
/readfile <id> - Read a saved text/PDF/DOCX/XLSX document
/createfile <filename> <content> - Create a sandboxed .txt/.md/.json/.csv/.pdf/.docx/.xlsx file
/indexfile <id> - Index one saved document for Q&A
/indexdocs - Index all your saved documents for Q&A
/docchunks <id> - Show index status for one document
/askfile <id> <question> - Ask a question about one document
/askdocs <question> - Ask across all indexed documents
/summarizefile <id> - Summarize one indexed document
/summarizedocs - Summarize all indexed documents
/tools - List available safe agent tools
/pending - List actions waiting for your approval
/approve <id> - Approve a pending risky action
/deny <id> - Deny a pending risky action
/plan <goal> - Create a step-by-step task plan
/tasks - List active task plans
/task <id> - Show a task plan
/done <task-id> [step-number] - Mark a task or step done
/cancel <task-id> - Cancel a task plan

Normal messages are answered by the local Ollama model.
""";

        return Task.FromResult(new CommandResult(true, reply));
    }
}
