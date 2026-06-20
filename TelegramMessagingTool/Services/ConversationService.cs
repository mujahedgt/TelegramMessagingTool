using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class ConversationService
{
    public async Task<List<OllamaMessageDto>> CreateConversationContextAsync(
        TelegramDbContext dbContext,
        int connectedUserId,
        int maxHistory,
        CancellationToken cancellationToken,
        string toolInstructions = "")
    {
        List<ChatMessage> history = await dbContext.Messages
            .Where(x => x.ConnectedUserId == connectedUserId)
            .OrderByDescending(x => x.Timestamp)
            .Take(maxHistory)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(cancellationToken);

        List<Memory> memories = await dbContext.Memories
            .Where(x => x.ConnectedUserId == connectedUserId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        List<OllamaMessageDto> messages =
        [
            new OllamaMessageDto(
                "system",
                BuildSystemPrompt(memories, toolInstructions))
        ];

        messages.AddRange(history.Select(x => new OllamaMessageDto(
            RoleToOllamaRole(x.Role),
            x.Content)));

        return messages;
    }

    public static string BuildSystemPrompt(IReadOnlyList<Memory> memories, string toolInstructions = "")
    {
        string prompt = """
You are a helpful Telegram assistant running inside TelegramMessagingTool.
Answer clearly, briefly, and honestly.

Current capabilities:
- You can answer normal chat messages using the conversation history and saved memories below.
- Users can manage the bot with these Available Telegram commands: /help, /status, /reset, /remember <fact>, /memory, /forget <id>, /files, /readfile <id>, /createfile <filename> <content>, /tools, /pending, /approve <id>, /deny <id>, /plan <goal>, /tasks, /task <id>, /done <task-id> [step-number], and /cancel <task-id>.
- You can use saved memories when they are relevant, but do not reveal private memory contents unless the user asks.
- You can request safe tools more than once when a task truly needs multiple steps. After each tool observation, either request one more safe tool with strict JSON or provide the final answer.

Safety rules:
- Do not claim that you browsed the web, inspected files, ran commands, or changed the system unless a tool result or command result actually provides that evidence.
- If you need current facts, prices, specs, market values, news, or external information, request the `online_search` tool instead of guessing.
- If the user misspells a clear known term, correct it silently in the search query and mention the correction in the final answer.
- If the user asks for an action this bot cannot perform yet, say what is currently supported and suggest the relevant command if one exists.
- Never invent command results, database status, file contents, prices, or external facts.
""";

        if (!string.IsNullOrWhiteSpace(toolInstructions))
        {
            prompt += "\n\nAgent tool instructions:\n" + toolInstructions.Trim();
        }

        if (memories.Count == 0)
        {
            return prompt;
        }

        string memoryText = string.Join("\n", memories.Select(x => $"- {x.Content}"));
        return prompt + "\n\nKnown memories about this user:\n" + memoryText;
    }

    public static string RoleToOllamaRole(ChatRoles role)
    {
        return role switch
        {
            ChatRoles.User => "user",
            ChatRoles.Assistant => "assistant",
            ChatRoles.System => "system",
            _ => "user"
        };
    }
}
