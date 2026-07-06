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
        if (!CommandParser.Matches(message.Text, Name))
        {
            return Task.FromResult(new CommandResult(false, null));
        }

        string reply = """
TelegramMessagingTool Agent Commands

/help - Show this help
/systeminfo - Show read-only local OS and runtime info
/diskstatus - Show read-only local disk space status
/processes [count] - Show read-only local running processes by memory
/killprocess <pid> - Create approval request to terminate a process later
/status - Show bot status
/reset - Clear your conversation history
/memory - Show saved memories
/remember <text> - Save a memory
/forget <id> - Delete a memory
/files - List saved files and uploaded documents
/images - List saved sandboxed image files
/describeimage <id> - Describe a saved image when image vision is enabled; otherwise show safe metadata
/voicefiles - List saved sandboxed audio files
/transcribe <id> - Transcribe a saved audio file when a trusted local provider is configured
/transcriptinsights <id> - Summarize and extract tasks from a saved transcript document
/speaktext <text> - Generate TTS audio into the sandbox without sending it automatically
/readfile <id> - Read a saved text/PDF/DOCX/XLSX document
/createfile <filename> <content> - Create a sandboxed .txt/.md/.json/.csv/.pdf/.docx/.xlsx file
/importfiles - Admin-only: list local ImportInbox files
/importfile <filename> - Admin-only: import one local ImportInbox file
/deletefile <id> - Create admin approval request to delete a sandboxed saved file
/indexfile <id> - Index one saved document for Q&A
/indexdocs - Index all your saved documents for Q&A
/docchunks <id> - Show index status for one document
/askfile <id> <question> - Ask a question about one document
/askdocs <question> - Ask across all indexed documents
/summarizefile <id> - Summarize one indexed document
/summarizedocs - Summarize all indexed documents
/embedfile <id> - Generate local embeddings for one indexed document
/embeddocs - Generate local embeddings for all indexed documents
/tools - List available safe agent tools
/harnesses - Show image and voice agent harness readiness
/plugins - Show plugin manifest/loading diagnostics
/pending - List actions waiting for your approval
/action <id> - Show pending/approved action audit details
/actions [count] - List recent approval/action audit records
/approve <id> - Approve a pending risky action
/deny <id> - Deny a pending risky action
/plan <goal> - Create a step-by-step task plan
/tasks - List active task plans
/task <id> - Show a task plan
/schedule <task-id> <step-number> <time> [note] - Schedule a task step reminder time
/schedulelist - List scheduled task step reminders
/unschedule <task-id> <step-number> - Clear a scheduled task step reminder
/done <task-id> [step-number] - Mark a task or step done
/cancel <task-id> - Cancel a task plan

Normal messages are answered by the local Ollama model.
In groups, Telegram commands addressed as /command@botusername are also accepted.
""";

        return Task.FromResult(new CommandResult(true, reply));
    }
}
