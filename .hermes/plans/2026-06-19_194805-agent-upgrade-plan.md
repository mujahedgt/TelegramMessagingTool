# TelegramMessagingTool Agent Upgrade Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Upgrade `TelegramMessagingTool` from a Telegram-to-Ollama chatbot into a safer lightweight Telegram AI agent with commands, memory, tool use, approvals, and task execution foundations.

**Architecture:** Refactor the current single-file flow into small services while keeping the app as a .NET console bot. Add agent capabilities incrementally: command routing first, then memory, then safe tool calling, then approval-gated dangerous tools, then simple planning/task tracking.

**Tech Stack:** C# / .NET 10, Telegram.Bot, EF Core SQL Server LocalDB, Ollama chat API, dependency-free console test project.

---

## Current Context

Project root:

```text
C:/temp/TelegramMessagingTool
```

Important files currently present:

```text
C:/temp/TelegramMessagingTool/TelegramMessagingTool.slnx
C:/temp/TelegramMessagingTool/README.md
C:/temp/TelegramMessagingTool/.gitignore
C:/temp/TelegramMessagingTool/TelegramMessagingTool/Program.cs
C:/temp/TelegramMessagingTool/TelegramMessagingTool/BotRuntime.cs
C:/temp/TelegramMessagingTool/TelegramMessagingTool/Data/DbContext.cs
C:/temp/TelegramMessagingTool/TelegramMessagingTool/models/*.cs
C:/temp/TelegramMessagingTool/TelegramMessagingTool.Tests/Program.cs
C:/temp/TelegramMessagingTool/TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj
```

Known verified state before this plan:

```text
Tests: dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
Result: All TelegramMessagingTool helper tests passed.

Build: dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
Result: Build succeeded. 0 warnings, 0 errors.

Vulnerability scan: dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
Result: no vulnerable packages.
```

Runtime blocker:

```text
TELEGRAM_BOT_TOKEN is not set in the current Hermes terminal environment, so the bot cannot be fully runtime-verified until the token is provided.
```

---

## Guiding Principles

1. **Keep it safe by default.** No shell/file-write/delete/network side effects without explicit approval.
2. **Keep each change small.** Build and run tests after every task.
3. **Do not build a huge framework first.** Add minimal interfaces and implementations that support the next feature.
4. **Prefer environment configuration.** Do not hard-code secrets, model names, paths, or admin IDs.
5. **Do not log private content by default.** Keep `LOG_MESSAGE_CONTENT=false` default.
6. **Use TDD for helper/service behavior.** The current test project is simple but enough to grow.

---

## Phase 1 — Stabilize Architecture

### Task 1: Create service folders and move shared DTOs

**Objective:** Prepare the project structure for agent features without changing behavior.

**Files:**

- Create: `TelegramMessagingTool/Services/.gitkeep`
- Create: `TelegramMessagingTool/Agent/.gitkeep`
- Create: `TelegramMessagingTool/Commands/.gitkeep`
- Create: `TelegramMessagingTool/Tools/.gitkeep`
- Modify: `TelegramMessagingTool/Program.cs`
- Create: `TelegramMessagingTool/OllamaMessageDto.cs`

**Steps:**

1. Create folders:

```text
TelegramMessagingTool/Services
TelegramMessagingTool/Agent
TelegramMessagingTool/Commands
TelegramMessagingTool/Tools
```

2. Move the bottom-of-file record from `Program.cs`:

```csharp
public record OllamaMessageDto(string role, string content);
```

to new file:

```csharp
namespace TelegramMessagingTool;

public record OllamaMessageDto(string role, string content);
```

3. Remove the old record from `Program.cs`.

**Verify:**

```bash
cd /c/temp/TelegramMessagingTool
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
```

Expected:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

---

### Task 2: Extract Ollama client service

**Objective:** Move Ollama request/response logic out of `Program.cs`.

**Files:**

- Create: `TelegramMessagingTool/Services/OllamaChatClient.cs`
- Modify: `TelegramMessagingTool/Program.cs`
- Modify: `TelegramMessagingTool.Tests/Program.cs`

**Step 1: Add tests for response parsing helper**

Add a test for valid Ollama JSON:

```csharp
string validJson = """
{
  "message": {
    "role": "assistant",
    "content": "Hello from Ollama"
  }
}
""";

AssertEqual("Hello from Ollama", OllamaChatClient.ParseAssistantContent(validJson), "ParseAssistantContent reads assistant content");
```

Add a test for missing/invalid content:

```csharp
AssertEqual("Invalid response received from Ollama.", OllamaChatClient.ParseAssistantContent("not json"), "ParseAssistantContent handles invalid JSON");
```

Run and verify failure first:

```bash
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
```

Expected: fail because `OllamaChatClient` does not exist yet.

**Step 2: Implement service**

Create:

```csharp
using System.Text;
using System.Text.Json;

namespace TelegramMessagingTool.Services;

public sealed class OllamaChatClient
{
    private readonly HttpClient _httpClient;
    private readonly BotSettings _settings;

    public OllamaChatClient(HttpClient httpClient, BotSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<string> AskAsync(List<OllamaMessageDto> conversationContext, CancellationToken cancellationToken)
    {
        var ollamaRequest = new
        {
            model = _settings.OllamaModel,
            stream = false,
            options = new
            {
                temperature = 0.2
            },
            messages = conversationContext
        };

        string requestJson = JsonSerializer.Serialize(ollamaRequest);

        using StringContent httpContent = new(requestJson, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.PostAsync(
            _settings.OllamaUrl,
            httpContent,
            cancellationToken);

        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return $"Ollama returned an error: {(int)response.StatusCode} {response.ReasonPhrase}";
        }

        return ParseAssistantContent(responseText);
    }

    public static string ParseAssistantContent(string responseText)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseText);

            string? content = document
                .RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content)
                ? "Empty response from Ollama."
                : content.Trim();
        }
        catch (JsonException)
        {
            return "Invalid response received from Ollama.";
        }
        catch (KeyNotFoundException)
        {
            return "Invalid response received from Ollama.";
        }
        catch (InvalidOperationException)
        {
            return "Invalid response received from Ollama.";
        }
    }
}
```

**Step 3: Wire Program.cs**

Replace local `AskOllamaAsync` logic with:

```csharp
var ollamaClient = new OllamaChatClient(qwenClient, settings);
```

Then use:

```csharp
string finalAnswer = await ollamaClient.AskAsync(conversationContext, cancellationToken);
```

Remove `AskOllamaAsync` from `Program.cs`.

**Verify:**

```bash
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
```

Expected: tests pass and build succeeds.

---

### Task 3: Extract conversation service

**Objective:** Move conversation history construction out of `Program.cs`.

**Files:**

- Create: `TelegramMessagingTool/Services/ConversationService.cs`
- Modify: `TelegramMessagingTool/Program.cs`

**Implementation:**

Create:

```csharp
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
        CancellationToken cancellationToken)
    {
        List<ChatMessage> history = await dbContext.Messages
            .Where(x => x.ConnectedUserId == connectedUserId)
            .OrderByDescending(x => x.Timestamp)
            .Take(maxHistory)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(cancellationToken);

        List<OllamaMessageDto> messages =
        [
            new OllamaMessageDto(
                "system",
                "You are a helpful Telegram assistant. Answer clearly and briefly. When tools are available, request them using the documented JSON tool-call format.")
        ];

        messages.AddRange(history.Select(x => new OllamaMessageDto(
            RoleToOllamaRole(x.Role),
            x.Content)));

        return messages;
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
```

Update `Program.cs` to instantiate and use `ConversationService`. Remove old `CreateConversationContextAsync` and `RoleToOllamaRole` local functions.

**Verify:**

```bash
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
```

Expected: build succeeds.

---

## Phase 2 — Telegram Commands

### Task 4: Add command result and command handler interface

**Objective:** Create a simple command framework for slash commands.

**Files:**

- Create: `TelegramMessagingTool/Commands/CommandResult.cs`
- Create: `TelegramMessagingTool/Commands/IBotCommand.cs`

**Implementation:**

```csharp
namespace TelegramMessagingTool.Commands;

public sealed record CommandResult(bool Handled, string? ReplyText);
```

```csharp
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public interface IBotCommand
{
    string Name { get; }
    string Description { get; }

    Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken);
}
```

**Verify:**

```bash
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
```

---

### Task 5: Add `/help` command

**Objective:** Let users see bot capabilities.

**Files:**

- Create: `TelegramMessagingTool/Commands/HelpCommand.cs`
- Create: `TelegramMessagingTool/Commands/CommandRouter.cs`
- Modify: `TelegramMessagingTool/Program.cs`

**Implementation:**

`HelpCommand.cs`:

```csharp
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
/tools - List available tools

Normal messages are answered by the local Ollama model.
""";

        return Task.FromResult(new CommandResult(true, reply));
    }
}
```

`CommandRouter.cs`:

```csharp
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class CommandRouter
{
    private readonly IReadOnlyList<IBotCommand> _commands;

    public CommandRouter(IReadOnlyList<IBotCommand> commands)
    {
        _commands = commands;
    }

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        foreach (IBotCommand command in _commands)
        {
            CommandResult result = await command.TryHandleAsync(message, user, dbContext, cancellationToken);
            if (result.Handled)
            {
                return result;
            }
        }

        return new CommandResult(false, null);
    }
}
```

Wire in `Program.cs` after services are created:

```csharp
var commandRouter = new CommandRouter([
    new HelpCommand()
]);
```

After user is loaded/created but before saving the user message to history:

```csharp
CommandResult commandResult = await commandRouter.TryHandleAsync(message, user, dbContext, cancellationToken);
if (commandResult.Handled)
{
    if (!string.IsNullOrWhiteSpace(commandResult.ReplyText))
    {
        foreach (string chunk in TelegramMessageFormatter.SplitForTelegram(commandResult.ReplyText))
        {
            await bot.SendMessage(message.Chat.Id, chunk, cancellationToken: cancellationToken);
        }
    }

    return;
}
```

**Verify:**

```bash
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
```

Manual runtime verification after `TELEGRAM_BOT_TOKEN` is set:

```text
Send /help to the bot.
Expected: command list is returned without calling Ollama.
```

---

### Task 6: Add `/status` command

**Objective:** Show operational status: model, DB, allowlist, logging mode.

**Files:**

- Create: `TelegramMessagingTool/Commands/StatusCommand.cs`
- Modify: `TelegramMessagingTool/Program.cs`

**Implementation:**

```csharp
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class StatusCommand : IBotCommand
{
    private readonly BotSettings _settings;

    public StatusCommand(BotSettings settings)
    {
        _settings = settings;
    }

    public string Name => "/status";
    public string Description => "Show bot runtime status.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!message.Text?.StartsWith("/status", StringComparison.OrdinalIgnoreCase) ?? true)
        {
            return new CommandResult(false, null);
        }

        bool dbReady = await dbContext.Database.CanConnectAsync(cancellationToken);

        string reply = $"""
Status

Database: {(dbReady ? "OK" : "Unavailable")}
Ollama URL: {_settings.OllamaUrl}
Ollama model: {_settings.OllamaModel}
Allowlist: {(_settings.AllowedChatIds.Count == 0 ? "disabled" : $"enabled ({_settings.AllowedChatIds.Count} chat IDs)")}
Message content logging: {(_settings.LogMessageContent ? "enabled" : "disabled")}
Apply migrations: {_settings.ApplyMigrations}
""";

        return new CommandResult(true, reply);
    }
}
```

Add to router:

```csharp
new StatusCommand(settings)
```

**Verify:** build succeeds; manually send `/status` after token is configured.

---

### Task 7: Add `/reset` command

**Objective:** Allow users to clear their own conversation history.

**Files:**

- Create: `TelegramMessagingTool/Commands/ResetCommand.cs`
- Modify: `TelegramMessagingTool/Program.cs`

**Implementation:**

```csharp
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Commands;

public sealed class ResetCommand : IBotCommand
{
    public string Name => "/reset";
    public string Description => "Clear your conversation history.";

    public async Task<CommandResult> TryHandleAsync(
        Message message,
        ConnectedUser user,
        TelegramDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!message.Text?.StartsWith("/reset", StringComparison.OrdinalIgnoreCase) ?? true)
        {
            return new CommandResult(false, null);
        }

        int deleted = await dbContext.Messages
            .Where(x => x.ConnectedUserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        return new CommandResult(true, $"Conversation reset. Deleted {deleted} stored messages.");
    }
}
```

Add to router.

**Verify:** build succeeds; manually send `/reset` after token is configured.

---

## Phase 3 — Memory System

### Task 8: Add Memory entity

**Objective:** Persist long-term facts separately from chat history.

**Files:**

- Create: `TelegramMessagingTool/models/Memory.cs`
- Modify: `TelegramMessagingTool/Data/DbContext.cs`

**Implementation:**

```csharp
namespace TelegramMessagingTool.Models;

public class Memory
{
    public int Id { get; set; }

    public int ConnectedUserId { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ConnectedUser User { get; set; } = null!;
}
```

Add DbSet:

```csharp
public DbSet<Memory> Memories => Set<Memory>();
```

Add model configuration:

```csharp
modelBuilder.Entity<Memory>(entity =>
{
    entity.HasKey(x => x.Id);

    entity.Property(x => x.Content)
        .HasMaxLength(1000);

    entity.Property(x => x.CreatedAt)
        .HasDefaultValueSql("GETUTCDATE()");

    entity.Property(x => x.UpdatedAt)
        .HasDefaultValueSql("GETUTCDATE()");

    entity.HasIndex(x => x.ConnectedUserId);

    entity.HasOne(x => x.User)
        .WithMany()
        .HasForeignKey(x => x.ConnectedUserId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

Create migration:

```bash
cd /c/temp/TelegramMessagingTool
dotnet ef migrations add AddMemories --project TelegramMessagingTool/TelegramMessagingTool.csproj
```

If `dotnet ef` is unavailable, install tool or use Visual Studio Package Manager Console later.

**Verify:**

```bash
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
```

---

### Task 9: Add memory commands

**Objective:** Users can save, list, and delete durable memories.

**Files:**

- Create: `TelegramMessagingTool/Commands/RememberCommand.cs`
- Create: `TelegramMessagingTool/Commands/MemoryCommand.cs`
- Create: `TelegramMessagingTool/Commands/ForgetCommand.cs`
- Modify: `TelegramMessagingTool/Program.cs`

**Behavior:**

```text
/remember User is learning C#
/memory
/forget 12
```

**Important rules:**

- Limit memory content to 1000 chars.
- Do not auto-save every chat message as memory.
- Only save explicit `/remember` commands at first.

**Verify manually:**

1. Send `/remember I am learning C#`.
2. Send `/memory`.
3. Confirm saved memory appears.
4. Send `/forget <id>`.
5. Confirm memory is removed.

---

### Task 10: Inject memories into model context

**Objective:** Make Ollama aware of saved user memories.

**Files:**

- Modify: `TelegramMessagingTool/Services/ConversationService.cs`

**Implementation idea:**

Before chat history, load up to 10 most recent memories:

```csharp
List<Memory> memories = await dbContext.Memories
    .Where(x => x.ConnectedUserId == connectedUserId)
    .OrderByDescending(x => x.UpdatedAt)
    .Take(10)
    .ToListAsync(cancellationToken);
```

Add to system message:

```text
Known memories about this user:
- ...
```

**Verify:**

1. Save memory with `/remember`.
2. Ask a question that should use the memory.
3. Confirm model receives context. Runtime needs token and Ollama.

---

## Phase 4 — Safe Tool System

### Task 11: Add tool abstractions

**Objective:** Create a minimal tool registry for agent actions.

**Files:**

- Create: `TelegramMessagingTool/Tools/AgentToolDefinition.cs`
- Create: `TelegramMessagingTool/Tools/IAgentTool.cs`
- Create: `TelegramMessagingTool/Tools/ToolResult.cs`
- Create: `TelegramMessagingTool/Tools/ToolRegistry.cs`

**Implementation:**

```csharp
namespace TelegramMessagingTool.Tools;

public sealed record ToolResult(bool Success, string Output, bool RequiresApproval = false);
```

```csharp
namespace TelegramMessagingTool.Tools;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    bool RequiresApproval { get; }

    Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken);
}
```

```csharp
namespace TelegramMessagingTool.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IAgentTool> Tools => _tools.Values;

    public bool TryGet(string name, out IAgentTool? tool)
    {
        return _tools.TryGetValue(name, out tool);
    }
}
```

**Verify:** build succeeds.

---

### Task 12: Add safe tools: datetime and calculator

**Objective:** Add first safe tools that require no approval.

**Files:**

- Create: `TelegramMessagingTool/Tools/DateTimeTool.cs`
- Create: `TelegramMessagingTool/Tools/CalculatorTool.cs`
- Modify: `TelegramMessagingTool.Tests/Program.cs`

**Calculator scope:** Keep it simple. Support only arithmetic expressions with digits, spaces, `+ - * / ( ) .`. Do not use `eval`; C# has no eval, but avoid shelling out.

For first version, use `DataTable.Compute` only after strict input validation, or implement a tiny parser later.

**Test cases:**

```csharp
AssertTrue((await new DateTimeTool().ExecuteAsync("", CancellationToken.None)).Success, "DateTimeTool succeeds");
AssertEqual("4", (await new CalculatorTool().ExecuteAsync("2+2", CancellationToken.None)).Output, "CalculatorTool calculates 2+2");
```

**Verify:** tests pass and build succeeds.

---

### Task 13: Add `/tools` command

**Objective:** Let users see available tools.

**Files:**

- Create: `TelegramMessagingTool/Commands/ToolsCommand.cs`
- Modify: `TelegramMessagingTool/Program.cs`

**Behavior:**

```text
/tools
```

Response:

```text
Available tools:
- datetime: Returns current local/UTC time
- calculator: Calculates safe arithmetic expressions
```

**Verify:** build succeeds; manual runtime test after token is configured.

---

### Task 14: Add JSON tool-call parser

**Objective:** Detect when the model is asking for a tool call.

**Files:**

- Create: `TelegramMessagingTool/Agent/ToolCallRequest.cs`
- Create: `TelegramMessagingTool/Agent/ToolCallParser.cs`
- Modify: `TelegramMessagingTool.Tests/Program.cs`

**Expected model format:**

```json
{
  "type": "tool_call",
  "tool": "calculator",
  "input": "2+2"
}
```

**Implementation:**

```csharp
namespace TelegramMessagingTool.Agent;

public sealed record ToolCallRequest(string Tool, string Input);
```

`ToolCallParser.TryParse(string text, out ToolCallRequest? request)` should:

- parse valid JSON only
- require `type == "tool_call"`
- require non-empty `tool`
- default `input` to empty string
- return false on normal text

**Tests:**

```csharp
string json = """{"type":"tool_call","tool":"calculator","input":"2+2"}""";
AssertTrue(ToolCallParser.TryParse(json, out ToolCallRequest? request), "ToolCallParser parses tool call");
AssertEqual("calculator", request!.Tool, "ToolCallParser extracts tool name");
AssertFalse(ToolCallParser.TryParse("hello", out _), "ToolCallParser ignores normal text");
```

**Verify:** tests pass and build succeeds.

---

### Task 15: Add single-step tool execution loop

**Objective:** Allow the model to call one safe tool, then return the tool result back to the model for final response.

**Files:**

- Create: `TelegramMessagingTool/Agent/AgentRunner.cs`
- Modify: `TelegramMessagingTool/Program.cs`
- Modify: `TelegramMessagingTool/Services/ConversationService.cs`

**Behavior:**

1. Ask Ollama for a response.
2. If response is normal text, return it.
3. If response is JSON tool call:
   - look up tool
   - run tool if safe
   - append tool result as a user/system message
   - ask Ollama once more
4. Return final text.

**Limit:** Only one tool call in first version. This prevents infinite loops.

**System prompt addition:**

```text
If you need a tool, respond ONLY as JSON:
{"type":"tool_call","tool":"tool_name","input":"input text"}
Available tools:
- datetime: Get current date/time
- calculator: Calculate simple arithmetic
After receiving tool result, answer normally.
```

**Verify:** build succeeds. Manual runtime test after token/Ollama:

```text
Ask: What is 25 * 19?
Expected: model may call calculator and then answer 475.
```

---

## Phase 5 — Approval System for Risky Tools

### Task 16: Add PendingAction entity

**Objective:** Store approval requests for future dangerous tools.

**Files:**

- Create: `TelegramMessagingTool/models/PendingAction.cs`
- Modify: `TelegramMessagingTool/Data/DbContext.cs`
- Add EF migration: `AddPendingActions`

**Entity:**

```csharp
namespace TelegramMessagingTool.Models;

public class PendingAction
{
    public int Id { get; set; }
    public int ConnectedUserId { get; set; }
    public long ChatId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}
```

**Verify:** build succeeds and migration compiles.

---

### Task 17: Add `/approve` and `/deny` commands

**Objective:** Allow user/admin to approve or deny pending actions.

**Files:**

- Create: `TelegramMessagingTool/Commands/ApproveCommand.cs`
- Create: `TelegramMessagingTool/Commands/DenyCommand.cs`
- Modify: `TelegramMessagingTool/Program.cs`

**First version behavior:**

- `/approve 12` marks action 12 approved.
- `/deny 12` marks action 12 denied.
- Do not execute approved action yet in this task.

**Verify:** manual DB/status test once token is configured.

---

### Task 18: Add approval-aware tool execution

**Objective:** If a tool requires approval, create a pending action instead of running it.

**Files:**

- Modify: `TelegramMessagingTool/Agent/AgentRunner.cs`
- Modify: `TelegramMessagingTool/Tools/IAgentTool.cs`

**Behavior:**

If `tool.RequiresApproval == true`:

```text
Action requires approval:
Tool: file_read
Input: C:\temp\example.txt
Approve with /approve 12 or deny with /deny 12.
```

**Verify:** add a fake approval-required test tool in tests and assert it creates approval message.

---

## Phase 6 — Simple Task Planning

### Task 19: Add AgentTask and AgentTaskStep entities

**Objective:** Store multi-step goals.

**Files:**

- Create: `TelegramMessagingTool/models/AgentTask.cs`
- Create: `TelegramMessagingTool/models/AgentTaskStep.cs`
- Modify: `TelegramMessagingTool/Data/DbContext.cs`
- Add EF migration: `AddAgentTasks`

**First version fields:**

```text
AgentTask: Id, ConnectedUserId, Goal, Status, CreatedAt, CompletedAt
AgentTaskStep: Id, AgentTaskId, StepNumber, Description, Status, Result
```

**Verify:** build succeeds and migration compiles.

---

### Task 20: Add `/plan` command

**Objective:** Let the bot create a plan for complex tasks without executing it yet.

**Files:**

- Create: `TelegramMessagingTool/Commands/PlanCommand.cs`
- Modify: `TelegramMessagingTool/Program.cs`

**Behavior:**

```text
/plan Review my C# project and suggest improvements
```

The command should:

1. Ask Ollama to break goal into 3-7 steps.
2. Save task and steps to DB.
3. Return plan with task ID.

**Verify:** manual runtime test after token/Ollama.

---

### Task 21: Add `/tasks` command

**Objective:** Show current tasks and statuses.

**Files:**

- Create: `TelegramMessagingTool/Commands/TasksCommand.cs`

**Behavior:**

```text
/tasks
```

Response:

```text
Open tasks:
#3 Review my C# project - Planned - 5 steps
```

**Verify:** manual runtime test.

---

## Phase 7 — Admin and Production Readiness

### Task 22: Add admin-only command guard

**Objective:** Restrict sensitive commands to admin chat ID.

**Files:**

- Create: `TelegramMessagingTool/Commands/AdminOnlyCommand.cs` or helper in `CommandRouter`
- Modify: admin commands once created

**Admin-only commands:**

```text
/approve
/deny
/users
/block
/allow
```

**Rule:** If `ADMIN_CHAT_ID` is `0`, no admin-only command should run.

**Verify:** unit-test guard helper and manually test after token is set.

---

### Task 23: Add `/users` admin command

**Objective:** Let admin list connected users.

**Files:**

- Create: `TelegramMessagingTool/Commands/UsersCommand.cs`

**Behavior:**

```text
/users
```

Only admin can run. Shows:

```text
Users:
- ChatId: ..., Username: ..., LastSeenAt: ...
```

**Privacy:** Do not show message content.

---

### Task 24: Add README agent section

**Objective:** Document the new agent features.

**Files:**

- Modify: `README.md`

**Add sections:**

- Agent modes
- Commands
- Tool-calling format
- Memory behavior
- Approval behavior
- Safety model
- Runtime examples

**Verify:** read README and ensure commands match implementation.

---

## Phase 8 — Release and Verification

### Task 25: Full verification pass

**Objective:** Ensure project is still healthy.

**Commands:**

```bash
cd /c/temp/TelegramMessagingTool
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
```

Expected:

```text
All TelegramMessagingTool helper tests passed.
Build succeeded. 0 Warning(s), 0 Error(s).
No vulnerable packages.
```

---

### Task 26: Publish timestamped release

**Objective:** Create a release without deleting old releases.

**Command:**

```bash
cd /c/temp/TelegramMessagingTool
release_dir="release/TelegramMessagingTool-$(date +%Y%m%d-%H%M%S)"
dotnet publish TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --output "$release_dir" --nologo
printf '%s' "$release_dir" > .latest-release
```

Expected:

```text
TelegramMessagingTool -> C:\temp\TelegramMessagingTool\release\TelegramMessagingTool-YYYYMMDD-HHMMSS\
```

---

### Task 27: Runtime verification after token is available

**Objective:** Start the released bot and verify basic commands.

**Precondition:** User must provide or set `TELEGRAM_BOT_TOKEN`.

**Commands:**

```bash
cd /c/temp/TelegramMessagingTool
latest=$(cat .latest-release)
"$latest/TelegramMessagingTool.exe"
```

Manual Telegram checks:

```text
/help
/status
/reset
/tools
/remember I am learning C#
/memory
What is 2+2?
```

Expected:

- Bot starts successfully.
- Commands respond without crashing.
- Memory commands persist data.
- Tool-capable questions work if Ollama follows tool-call format.

---

## Risks and Tradeoffs

1. **Ollama model may not reliably emit JSON tool calls.** Mitigation: keep parser strict and fall back to normal text. Later add retries or a smaller deterministic tool-call prompt.
2. **EF migrations require `dotnet ef`.** If unavailable, install EF tools or generate migrations from Visual Studio.
3. **Telegram token is required for runtime validation.** Build/test can be verified without it, but live bot behavior cannot.
4. **File/system tools are risky.** Start with safe tools only. Add approval system before dangerous tools.
5. **Single-process console app is limited for production.** Later consider Windows service, systemd, Docker, or a hosted worker.

---

## Recommended Implementation Order

If time is limited, implement only these first:

1. Phase 1: architecture extraction
2. Phase 2: `/help`, `/status`, `/reset`
3. Phase 3: `/remember`, `/memory`, `/forget`
4. Phase 4: safe tool system with datetime/calculator
5. Phase 8: verify and publish

This gives the highest value quickly and makes the bot feel more agent-like without introducing dangerous capabilities too early.

---

## Definition of Done

The upgrade is done when:

- `dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo` passes.
- `dotnet build TelegramMessagingTool.slnx --configuration Release --nologo` succeeds with 0 errors.
- `dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive` reports no vulnerable packages.
- README documents all commands and configuration.
- A timestamped release is published under `C:/temp/TelegramMessagingTool/release/`.
- With `TELEGRAM_BOT_TOKEN` set, the bot starts and responds to `/help`, `/status`, `/reset`, `/memory`, and `/tools`.
