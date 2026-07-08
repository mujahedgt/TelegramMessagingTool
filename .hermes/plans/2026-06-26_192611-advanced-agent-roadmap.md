# Advanced Telegram Agent Roadmap Implementation Plan

> **For Hermes:** Use subagent-driven-development or normal TDD implementation task-by-task. Do not implement the whole roadmap in one patch.

**Goal:** Add a clear staged plan for scalable retrieval, multi-model routing, scheduled tasks, export, Telegram UX improvements, streaming responses, and a better console interface.

**Architecture:** Keep the bot safe and incremental. Add planning/config abstractions first, then small commands/services with tests, then provider-specific integrations behind feature flags. Do not replace working LocalDB/Ollama behavior until each new path has a fallback.

**Tech Stack:** C#/.NET 10, Telegram.Bot, EF Core, SQL Server LocalDB now; optional future PostgreSQL + pgvector, Qdrant/Chroma HTTP clients, Ollama local models, PDF/DOCX export libraries, Telegram inline keyboards/reactions.

---

## Current Context

The project already has:

- Telegram long-polling bot connected to local Ollama.
- SQL Server LocalDB persistence for users, messages, memories, uploaded files, document chunks, pending actions, and agent tasks.
- Local embeddings stored as `DocumentChunk.EmbeddingJson`.
- Document Q&A commands: `/indexfile`, `/indexdocs`, `/askfile`, `/askdocs`, `/embedfile`, `/embeddocs`.
- Approval workflow: `/pending`, `/action`, `/approve`, `/deny`.
- Task planner commands: `/plan`, `/tasks`, `/task`, `/done`, `/cancel`.
- Sandboxed file handling plus `/images` foundation for image-agent work.
- Console startup panel and local console input loop.

Main rule: preserve the current safe behavior while adding optional advanced paths.

---

# Recommended Priority Order

| Phase | Feature | Why first/now |
|---:|---|---|
| 1 | Multi-model routing | Low-risk configuration improvement; helps later features choose fast/large models. |
| 2 | Scheduled task reminders | Builds on existing AgentTask/AgentTaskStep models and gives visible value. |
| 3 | Inline keyboards + reactions | Improves current commands without changing AI core. |
| 4 | Export to Telegram | Uses existing conversation history; bounded feature with clear output. |
| 5 | Console improvements | Improves monitoring/debugging during bigger features. |
| 6 | Streamed responses | More invasive; requires Ollama streaming + Telegram edit/rate handling. |
| 7 | Vector DB support | Highest architecture impact; should be abstraction-first then provider-specific. |

---

# Phase 1: Multi-model Routing

## Status

- Task 1.1 and 1.2 are complete: route-specific settings, `ModelTaskKind`, `ModelRoutingService`, README docs, launcher env handoff, and `/status` model-route summary are implemented.
- Task 1.3 is complete: normal chat uses `Chat`, online-search final synthesis uses `ToolFinalAnswer`, document Q&A uses `DocumentQuestionAnswering`, and document summaries use `DocumentSummary`.

## Goal

Allow different model profiles for chat, planning, document Q&A, summarization, search finalization, image/voice future work, and embeddings.

## Proposed config

Environment variables:

```text
OLLAMA_MODEL_CHAT=qwen3:0.6b
OLLAMA_MODEL_PLAN=qwen3:4b
OLLAMA_MODEL_DOC_QA=qwen3:4b
OLLAMA_MODEL_SUMMARY=qwen3:4b
OLLAMA_MODEL_TOOL_FINAL=qwen3:0.6b
OLLAMA_MODEL_IMAGE=llava:latest
OLLAMA_MODEL_VOICE=qwen3:4b
OLLAMA_EMBEDDING_MODEL=nomic-embed-text
```

Fallback rule: if a task-specific model is missing, use existing `OLLAMA_MODEL`.

## Files likely to change

- Modify: `TelegramMessagingTool/BotRuntime.cs`
- Create: `TelegramMessagingTool/Services/ModelRoutingService.cs`
- Modify: `TelegramMessagingTool/Services/OllamaChatClient.cs`
- Modify: `TelegramMessagingTool/Services/DocumentQuestionAnsweringService.cs`
- Modify: `TelegramMessagingTool/Services/DocumentSummaryService.cs`
- Modify: `TelegramMessagingTool/Agent/AgentRunner.cs`
- Modify: `TelegramMessagingTool/Commands/StatusCommand.cs`
- Modify: `TelegramMessagingTool.Tests/Program.cs`
- Modify: `README.md`

## Bite-sized implementation tasks

### Task 1.1: Add model route config to settings

**Objective:** Extend `BotSettings` with route-specific model names using fallback to `OLLAMA_MODEL`.

**Test first:** Add tests that:

- default route model equals `OLLAMA_MODEL`
- `OLLAMA_MODEL_PLAN` overrides only plan route
- `/status` can show route summary without secrets

**Expected test command:**

```bash
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
```

### Task 1.2: Add `ModelTaskKind` and `ModelRoutingService`

**Objective:** Centralize model selection.

Suggested enum:

```csharp
public enum ModelTaskKind
{
    Chat,
    Planning,
    DocumentQuestionAnswering,
    DocumentSummary,
    ToolFinalAnswer,
    Image,
    Voice
}
```

Suggested service API:

```csharp
public sealed class ModelRoutingService
{
    public string GetModel(ModelTaskKind taskKind);
    public string RenderSummary();
}
```

### Task 1.3: Route existing model calls

**Objective:** Use task-specific models in:

- normal chat
- `/plan`
- `/askfile` and `/askdocs`
- `/summarizefile` and `/summarizedocs`
- online-search final answers

**Safety:** Do not let users set arbitrary model names from chat until admin config/allowlist exists.

---

# Phase 2: Scheduled Tasks and Telegram Reminders

## Status

- Task 2.1 is complete: `ScheduleParser` parses safe UTC schedule formats (`yyyy-MM-dd HH:mm`, `tomorrow HH:mm`, `in 30m`, `in 2h`) and rejects unsupported/past/zero-delay inputs.
- Task 2.2 is complete: `AgentTaskStep` now stores `ScheduledAtUtc`, `ReminderSentAtUtc`, and `ScheduleNote`; EF migration `AddAgentTaskStepScheduling` adds the fields and a schedule-time index; task rendering displays scheduled/reminded metadata.
- Task 2.3 is complete: `/schedule`, `/schedulelist`, and `/unschedule` manage scheduled task-step metadata without autonomous sending.
- Task 2.4 is complete: a duplicate-safe background reminder scanner sends due active task-step reminders and marks `ReminderSentAtUtc` only after successful Telegram send.

## Goal

Allow task steps to have scheduled execution/reminder times and run a background reminder loop that sends Telegram reminders.

## Data model proposal

Add fields to `AgentTaskStep`:

```csharp
public DateTime? ScheduledAtUtc { get; set; }
public DateTime? ReminderSentAtUtc { get; set; }
public string? ScheduleNote { get; set; }
```

Optional future table:

```text
ScheduledAgentEvent
- Id
- ConnectedUserId
- ChatId
- AgentTaskId
- AgentTaskStepId
- ScheduledAtUtc
- SentAtUtc
- Status: pending/sent/cancelled/failed
- ErrorMessage
```

Start simple with fields on `AgentTaskStep`; extract later if needed.

## Commands

```text
/schedule <task-id> <step-number> <time> [note]
/schedulelist
/unschedule <task-id> <step-number>
```

Examples:

```text
/schedule 4 2 tomorrow 9am review database migration
/schedule 4 3 2026-06-28 18:30 run verification
/schedulelist
/unschedule 4 2
```

## Files likely to change

- Modify: `TelegramMessagingTool/Models/AgentTaskStep.cs`
- Modify: `TelegramMessagingTool/Data/DbContext.cs`
- Add migration under `TelegramMessagingTool/Migrations/`
- Create: `TelegramMessagingTool/Services/ScheduleParser.cs`
- Create: `TelegramMessagingTool/Services/ScheduledTaskReminderService.cs`
- Create: `TelegramMessagingTool/Commands/ScheduleCommand.cs`
- Create: `TelegramMessagingTool/Commands/ScheduleListCommand.cs`
- Create: `TelegramMessagingTool/Commands/UnscheduleCommand.cs`
- Modify: `TelegramMessagingTool/Program.cs`
- Modify: `TelegramMessagingTool.Tests/Program.cs`
- Modify: `README.md`

## Bite-sized implementation tasks

### Task 2.1: Add schedule parser

**Objective:** Parse safe schedule formats first.

Initial supported formats:

```text
yyyy-MM-dd HH:mm
tomorrow HH:mm
in 30m
in 2h
```

Defer complex natural language.

### Task 2.2: Add fields and migration

**Objective:** Store scheduled time per task step.

Run:

```bash
dotnet ef migrations add AddScheduledTaskSteps --project TelegramMessagingTool/TelegramMessagingTool.csproj --startup-project TelegramMessagingTool/TelegramMessagingTool.csproj
```

### Task 2.3: Add commands

**Objective:** Let users schedule/list/unschedule reminders.

Tests:

- `/schedule` validates task ownership
- `/schedule` rejects invalid time
- `/schedulelist` shows due scheduled step
- `/unschedule` clears fields

### Task 2.4: Add background reminder loop

**Objective:** Every 30-60 seconds, find due unsent reminders and send Telegram messages.

Safety:

- only send to original `ChatId`
- mark sent after successful send
- log failures without crashing polling
- no arbitrary action execution yet, reminders only

---

# Phase 3: Telegram UX Improvements

## 3A: Inline Keyboards

### Status

- Task 3A.1 is complete: `CommandResult` supports optional `InlineKeyboardMarkup`, `InlineKeyboardFactory` can create pending-action buttons, `/pending` returns first-action button metadata, and Telegram sends markup on the first reply chunk.
- Task 3A.2 is complete: `PendingActionCallbackParser` parses compact pending-action callback data (`act:approve:<id>`, `act:deny:<id>`, `act:details:<id>`) and rejects malformed/unknown callbacks.
- Task 3A.3 is complete: callback queries for pending actions now answer Telegram callbacks and reuse the existing admin, ownership, approval, denial, details, and execution checks.
- Task 3A.4 is complete: task callback parsing and task inline keyboard metadata are prepared for `/tasks` and `/task` without executing task mutations from buttons yet.
- Task 3A.5 is complete: `task:open:<taskId>` callback handling is wired as a read-only action; `task:done` and `task:cancel` callbacks are acknowledged but intentionally do not mutate task state yet.
- Task 3A.6 is complete: `/task <id>` now includes step-specific `task:done-step:<taskId>:<stepNumber>` button metadata and parser support, still without mutating task state from buttons.
- Task 3A.7 is complete: `task:done-step:<taskId>:<stepNumber>` now safely marks only the selected owned step done via `AgentTaskService.MarkDoneAsync`; whole-task done and cancel buttons remain disabled placeholders.
- Task 3A.8 is complete: `task:done:<taskId>` now safely completes only the selected owned task via `AgentTaskService.MarkDoneAsync(..., stepNumber: null)`; cancel remains a disabled placeholder.
- Task 3A.9 is complete: `task:cancel:<taskId>` now safely cancels only the selected owned active task via `AgentTaskService.CancelAsync`.

### Goal

Replace action-heavy text replies with Telegram inline buttons.

## Target commands

| Command | Buttons |
|---|---|
| `/pending` | Approve, Deny, Details |
| `/tasks` | Open, Done, Cancel |
| `/files` | Read, Delete, Ask, Summarize |
| `/images` | Details, Future Describe |

## Callback data format

Keep it compact and signed/validated server-side:

```text
act:approve:<actionId>
act:deny:<actionId>
task:open:<taskId>
task:done:<taskId>
file:read:<fileId>
file:delete:<fileId>
```

Telegram callback data has size limits; do not put secrets or large payloads in it.

## Files likely to change

- Create: `TelegramMessagingTool/Telegram/InlineKeyboardFactory.cs`
- Create: `TelegramMessagingTool/Telegram/CallbackDataParser.cs`
- Create: `TelegramMessagingTool/Services/CallbackActionService.cs`
- Modify: `Program.cs` to handle `update.CallbackQuery`
- Modify command classes for optional keyboard result shape
- Modify `CommandResult` to support reply markup
- Add tests in `TelegramMessagingTool.Tests/Program.cs`

## Implementation tasks

1. Extend `CommandResult` with optional `InlineKeyboardMarkup`.
2. Add parser tests for callback data.
3. Update `/pending` only first.
4. Handle callback query for approve/deny/details.
5. Then expand to `/tasks` and `/files`.

---

## 3B: Message Reactions

### Status

- Task 3B.1 is complete: command results can carry optional reaction emoji metadata, `/approve`, `/deny`, `/done`, `/remember`, and `/reset` set lightweight reaction hints, and Telegram runtime sends best-effort message reactions without replacing important text replies.

### Goal

Use Telegram reaction API for lightweight acknowledgements.

Examples:

| Command | Reaction |
|---|---|
| `/done` | 👍 |
| `/remember` | ✅ |
| `/reset` | 🧹 or ✅ |
| `/deny` | 👎 |
| `/approve` | ✅ |

## Files likely to change

- Create: `TelegramMessagingTool/Services/TelegramReactionService.cs`
- Modify: `Program.cs` command handling
- Maybe extend `CommandResult` with `ReactionEmoji`
- Tests for command result metadata, not actual Telegram network call

## Safety

- Reactions should be best-effort.
- If Telegram rejects a reaction, log warning but still send normal command response if required.
- Do not replace important error/status text with only a reaction.

---

# Phase 4: Export to Telegram

## Status

- Task 4.1 is complete: `/exportchat txt [last N]` exports only the current user's recent persisted chat messages, clamps the count, saves the TXT file inside the existing document sandbox, returns it as a Telegram document attachment, and documents first-phase behavior.
- Task 4.2 is complete: `/exportchat docx [last N]` reuses the same privacy boundary and sandbox flow to generate an attached DOCX export through the existing OpenXML document support.
- Task 4.3 is complete: `/exportchat pdf [last N]` generates an attached PDF export through the existing safe PDF generation path while keeping the same current-user-only privacy boundary.

## Goal

Add:

```text
/exportchat [pdf|docx|txt] [last N]
```

The bot sends conversation history back to Telegram as a formatted file.

## Default behavior

```text
/exportchat
```

Should export the last 100 messages as DOCX or TXT initially.

Recommended staged rollout:

1. TXT export first: easiest, no new library.
2. DOCX export using existing `DocumentFormat.OpenXml` dependency.
3. PDF export later using a safe PDF generation approach.

## Files likely to change

- Create: `TelegramMessagingTool/Services/ChatExportService.cs`
- Create: `TelegramMessagingTool/Commands/ExportChatCommand.cs`
- Modify: `Program.cs` to support command result attachments or make command accept bot client through a service
- Modify: `README.md`
- Add tests in `TelegramMessagingTool.Tests/Program.cs`

## Safety/privacy

- Export only current user's conversation.
- Default `last 100`; clamp max, e.g. 500.
- Store temporary export files under project-root temp/export folder, not arbitrary paths.
- Delete temp files after send when possible.
- Warn that exports may contain private content.

---

# Phase 5: Console Improvements

## Status

- Task 5.1 is complete: the console now has a `/dashboard` local-only status view rendered through `RuntimeDashboardService`/`AgentConsoleRenderer`, including uptime, access mode, masked database summary, active tasks, pending approvals, indexed docs, saved files/images, recent warning/error count, and the standard event categories.
- Task 5.2 is complete: the console now has `/logs [count]` for local-only recent runtime events, rendered newest-first from the in-memory `RuntimeEventBuffer` with count clamping and existing token/secret redaction.

## Goal

Make the console more colorful, visually appealing, responsive, and easier to monitor logs/task statuses.

## Proposed improvements

- Color-coded sections and events.
- Live status refresh command:

```text
/dashboard
/tasks
/logs 20
/health
```

- Clear event categories:

```text
START, MESSAGE, COMMAND, TOOL, DOCUMENT, IMAGE, TASK, APPROVAL, ERROR, NET
```

- Compact status counters:

```text
Active tasks: 3
Pending approvals: 1
Indexed docs: 8
Saved images: 4
```

## Files likely to change

- Modify: `TelegramMessagingTool/ConsoleUi/AgentConsoleRenderer.cs`
- Create: `TelegramMessagingTool/ConsoleUi/ConsoleTheme.cs`
- Create: `TelegramMessagingTool/Services/RuntimeDashboardService.cs`
- Modify: `Program.cs` console input loop
- Tests for renderer output and secret masking

## Safety

- Continue masking DB credentials/secrets.
- Keep console usable in non-interactive/background mode.
- Closed stdin must never stop long polling.

---

# Phase 6: Streamed Responses

## Status

- Task 6.1 is complete: normal Telegram chat messages can send best-effort `typing...` chat actions while the local agent is generating a reply when `ENABLE_TELEGRAM_TYPING_INDICATOR=true`. Commands, file/document handling, and voice handling remain unwrapped. True streamed response chunks and edit-in-place behavior are still pending.

## Goal

Stream Ollama completions while showing Telegram typing indicator and optionally editing a placeholder message.

## Requirements

- Ollama streaming support in `OllamaChatClient`.
- Telegram typing action loop while waiting.
- Throttled message edits to avoid rate limits.
- Fallback to non-streamed response on error.

## Files likely to change

- Modify: `TelegramMessagingTool/Services/OllamaChatClient.cs`
- Create: `TelegramMessagingTool/Services/TelegramTypingService.cs`
- Create: `TelegramMessagingTool/Services/StreamingResponseService.cs`
- Modify: `Program.cs` normal-message handling
- Tests for parsing streamed JSON chunks

## Safe rollout

Feature flag:

```text
ENABLE_STREAMING_RESPONSES=false
```

Start with typing indicator only, then add edit-in-place streaming.

## Rate-limit strategy

- Send typing action every 4 seconds while model runs.
- If editing message, edit at most every 1.5-2 seconds.
- Final message always replaces/finishes the draft.

---

# Phase 7: Vector DB Support

## Goal

Move from `DocumentChunk.EmbeddingJson` to a scalable vector store.

## Provider options

| Provider | Pros | Cons | Recommended role |
|---|---|---|---|
| pgvector | SQL-native, strong consistency, good with Postgres | Requires moving from SQL Server or adding Postgres | Best long-term if app DB moves to Postgres |
| Qdrant | Purpose-built vector DB, good ANN, HTTP API | Extra service/deployment | Best standalone vector option |
| Chroma | Simple dev workflow | Less ideal for production | Good local experiment option |

## Recommendation

Start with an abstraction and Qdrant/Chroma-compatible external provider path, while keeping current `EmbeddingJson` fallback. Do not remove `EmbeddingJson` until vector DB path is stable.

## Interface proposal

```csharp
public interface IVectorStore
{
    Task UpsertAsync(DocumentVector vector, CancellationToken cancellationToken);
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(long chatId, IReadOnlyList<float> queryEmbedding, int limit, CancellationToken cancellationToken);
    Task DeleteByUploadedFileIdAsync(int uploadedFileId, CancellationToken cancellationToken);
}
```

Provider setting:

```text
VECTOR_STORE_PROVIDER=local_json|qdrant|chroma|pgvector
QDRANT_URL=http://localhost:6333
QDRANT_COLLECTION=telegram_documents
CHROMA_URL=http://localhost:8000
PGVECTOR_CONNECTION=...
```

## Files likely to change

- Create: `TelegramMessagingTool/Services/Vector/IVectorStore.cs`
- Create: `TelegramMessagingTool/Services/Vector/LocalJsonVectorStore.cs`
- Create: `TelegramMessagingTool/Services/Vector/QdrantVectorStore.cs`
- Later create: `ChromaVectorStore.cs`, `PgVectorStore.cs`
- Modify: `DocumentEmbeddingService.cs`
- Modify: `DocumentRetrievalService.cs`
- Modify: `PendingActionExecutor.cs` deletion cleanup
- Modify: `BotRuntime.cs` config
- Modify: `README.md`
- Add tests for provider selection and fallback

## Rollout tasks

### Task 7.1: Add vector-store abstraction with local fallback

No behavior change yet. Current `EmbeddingJson` remains default.

### Task 7.2: Add Qdrant provider behind feature flag

Use HTTP API and collection-per-bot or collection with payload filters:

```json
{
  "chatId": 123,
  "connectedUserId": 1,
  "uploadedFileId": 9,
  "chunkId": 44,
  "originalFileName": "contract.pdf"
}
```

### Task 7.3: Hybrid retrieval path

Search vector DB first when enabled; fall back to lexical + `EmbeddingJson` when provider fails.

### Task 7.4: Migration/cleanup plan

Add a `/vectorstatus` and `/reembeddocs` style command before removing old embeddings.

---

# Cross-cutting Safety and Quality Rules

1. Every feature gets tests first.
2. Every network/provider integration has a feature flag and fallback.
3. Telegram access remains fail-closed.
4. No secrets in logs, callback data, exported files, or roadmap docs.
5. Commands use exact parser matching and support `/command@botname`.
6. Every release must run:

```bash
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
dotnet ef migrations has-pending-model-changes --project TelegramMessagingTool/TelegramMessagingTool.csproj --startup-project TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --no-build
dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
```

7. Publish with timestamped release, update `.latest-release`, commit, push, restart, and verify exactly one latest bot process remains.

---

# Suggested Immediate Next Patch

Implement **Phase 3A Task 2** only:

- Add a compact `CallbackDataParser` for callback strings like `act:approve:<id>`, `act:deny:<id>`, and `act:details:<id>`.
- Add tests for valid callbacks, invalid prefixes, non-numeric IDs, and unknown verbs.
- Do not execute callbacks or edit Telegram messages yet.

This keeps callback parsing reviewable before wiring approve/deny/details actions.
