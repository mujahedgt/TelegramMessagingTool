# Post-Audit Telegram Agent Hardening Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Finish the next hardening wave for `TelegramMessagingTool` after the full project mismatch cleanup and sample plugin activation, prioritizing callback identity safety, risky machine-config visibility, test maintainability, operational diagnostics, media workflow polish, and plugin safety.

**Architecture:** Keep the bot incremental and TDD-driven. Each task should add focused helper tests first in `TelegramMessagingTool.Tests/Program.cs` or a newly split test file, implement one small behavior, then run the full verification/release pipeline before moving on.

**Tech Stack:** C#/.NET 10, Telegram.Bot, EF Core SQL Server LocalDB, Ollama local models, plugin assemblies through `TelegramMessagingTool.Abstractions`, Git/GitHub CLI workflow, Windows User environment runtime configuration.

---

## Current Context

- Repo: `C:\temp\TelegramMessagingTool`
- Branch: `master`
- Latest verified commit before this plan: `8a47025 Add sample dotnet project plugin tool`
- Latest release: `release/TelegramMessagingTool-20260706-203028`
- Plugin loading is enabled on Mujahed's machine through User env, not project defaults.
- Search direct-routing is configured on Mujahed's machine as `SEARCH_ROUTING_MODE=llm`.
- Sample plugin tools now include:
  - `sample_echo`
  - `dotnet_create_project`
- Project defaults intentionally remain safer than Mujahed's local machine configuration.

## Non-Negotiable Workflow for Each Implementation Patch

For every task that changes code:

1. Write/adjust tests first.
2. Run the specific test/helper suite and confirm RED when feasible.
3. Implement the smallest code change.
4. Run:
   ```bash
   cd /c/temp/TelegramMessagingTool
   dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
   dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
   dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
   dotnet list TelegramMessagingTool.Abstractions/TelegramMessagingTool.Abstractions.csproj package --vulnerable --include-transitive
   dotnet list plugins/SamplePlugin/TelegramMessagingTool.SamplePlugin.csproj package --vulnerable --include-transitive
   git diff --check
   ```
5. Publish a timestamped release:
   ```bash
   release_dir="release/TelegramMessagingTool-$(date +%Y%m%d-%H%M%S)"
   dotnet publish TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --output "$release_dir" --nologo
   printf '%s' "$release_dir" > .latest-release
   ```
6. Commit and push.
7. Restart latest release only with runtime env handoff.
8. Verify exactly one running `TelegramMessagingTool.exe`, path equals `.latest-release`, and Telegram API smoke send succeeds.

---

## Priority 8: Callback and Approval Identity Hardening

### Task 8.1: Authorize inline callback decisions by Telegram actor user ID

**Status:** Completed in commit `f31bfa2` (`Harden callback actor authorization`).

**Objective:** Prevent approval/action buttons in groups from being executed by a different Telegram user than the authorized actor/admin.

**Files:**
- Modify: `TelegramMessagingTool/Runtime/TelegramUpdateHandler.cs`
- Modify: `TelegramMessagingTool/Services/PendingActionCallbackService.cs`
- Modify: `TelegramMessagingTool/Services/TaskCallbackService.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs` or a new callback-focused test file if tests are split first

**Step 1: Write failing tests**

Add tests that simulate callback data where:

- `callbackQuery.Message.Chat.Id` is allowed/admin.
- `callbackQuery.From.Id` is not admin/owner.
- Approval callback is rejected.
- Task mutation callback is rejected when the actor is not the task owner/admin.

Expected RED: current callback handling relies too much on chat/message context and does not consistently enforce `callbackQuery.From.Id`.

**Step 2: Implement actor-aware authorization**

Add a small helper, likely near Telegram update handling:

```csharp
private static long GetCallbackActorId(CallbackQuery callbackQuery)
{
    return callbackQuery.From.Id;
}
```

Pass actor ID into callback services and verify against:

- `ADMIN_CHAT_ID`
- owning `ConnectedUser.TelegramUserId` where applicable
- existing allowlist/public-access rules only as an outer access check, not as approval authority

**Step 3: User-facing rejection**

Return a short Telegram callback answer/message like:

```text
This button is not authorized for your Telegram account.
```

Do not leak pending-action payloads.

**Step 4: Verification**

Run helper tests and a manual Telegram group/private callback test if possible.

**Commit:**

```bash
git add TelegramMessagingTool/Runtime/TelegramUpdateHandler.cs TelegramMessagingTool/Services/PendingActionCallbackService.cs TelegramMessagingTool/Services/TaskCallbackService.cs TelegramMessagingTool.Tests/Program.cs
git commit -m "Harden callback actor authorization"
```

---

### Task 8.2: Add callback audit metadata

**Status:** Completed in current patch (`Add callback decision observability`).

**Objective:** Record who clicked an approval/task callback and whether it was accepted/rejected.

**Files:**
- Modify: `TelegramMessagingTool/Services/RuntimeObservabilityService.cs`
- Modify: `TelegramMessagingTool/Services/PendingActionCallbackService.cs`
- Modify: `TelegramMessagingTool/Services/TaskCallbackService.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Plan:**

1. Add metadata-only observability event methods:
   - `CallbackDecisionReceived(...)`
   - `CallbackDecisionRejected(...)`
2. Include only IDs/type/status; do not log raw payloads or message text unless explicitly allowed by config.
3. Add tests that rendered log metadata does not include action payload JSON.

**Commit:**

```bash
git commit -m "Add callback decision observability"
```

---

## Priority 9: Risky Runtime Configuration Visibility

### Task 9.1: Add `/riskconfig` admin command

**Status:** Completed in current patch (`Add runtime risk configuration command`).

**Objective:** Since Mujahed enabled many local machine flags, add an admin-only command that shows risky enabled features clearly without exposing secrets.

**Files:**
- Create: `TelegramMessagingTool/Commands/RiskConfigCommand.cs`
- Modify: `TelegramMessagingTool/Runtime/CommandRouterFactory.cs`
- Modify: `TelegramMessagingTool/Commands/HelpCommand.cs`
- Modify: `README.md`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Command output should show:**

- `ALLOW_PUBLIC_ACCESS=true` warning
- `LOG_MESSAGE_CONTENT=true` warning
- `ENABLE_REPO_WRITE_TOOLS=true`
- `ENABLE_GITHUB_WRITE_TOOLS=true`
- `ENABLE_PLUGINS=true`
- `ENABLE_SAFE_COMMAND_TOOLS=true`
- `SEARCH_ROUTING_MODE=llm`
- Provider gates enabled but command missing:
  - `ENABLE_AUDIO_TRANSCRIPTION=true` + empty `AUDIO_TRANSCRIPTION_COMMAND`
  - `ENABLE_TEXT_TO_SPEECH=true` + empty `TEXT_TO_SPEECH_COMMAND`

**Output style:**

```text
Risk configuration summary
- Public access: ENABLED ⚠
- Message content logging: ENABLED ⚠
- Search routing: llm
- Plugin loading: ENABLED (trusted local DLLs only)
- Audio transcription: enabled, provider command missing
- TTS: enabled, provider command missing
```

**Security rule:** Never print token values, DB connection strings, or GitHub token values.

**Commit:**

```bash
git commit -m "Add runtime risk configuration command"
```

---

### Task 9.2: Add startup warning consolidation for risky machine config

**Status:** Completed in current patch (`Show consolidated runtime risk warnings`).

**Objective:** Startup panel should summarize risky local machine flags in one obvious place.

**Files:**
- Modify: `TelegramMessagingTool/ConsoleUi/AgentConsoleRenderer.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Plan:**

1. Extract risk-warning rendering into a reusable helper.
2. Reuse the helper for `/riskconfig` if practical.
3. Add tests for public access/content logging/repo write/plugin warnings.

**Commit:**

```bash
git commit -m "Show consolidated runtime risk warnings"
```

---

## Priority 10: Test Suite Maintainability Split

### Task 10.1: Split giant helper test runner into thematic files

**Status:** In progress — assertion helpers extracted to `TestAssert.cs`; plugin tests extracted to `PluginTests.cs` in `Extract plugin helper tests`; configuration/environment tests extracted to `ConfigurationTests.cs`; command-router factory tests extracted to `CommandTests.cs`; agent behavior eval tests extracted to `AgentBehaviorEvalTests.cs`; document/media command tests extracted to `DocumentTests.cs` in current patch.

**Objective:** Reduce `TelegramMessagingTool.Tests/Program.cs` size and make future TDD safer.

**Files:**
- Modify: `TelegramMessagingTool.Tests/Program.cs`
- Create likely files:
  - `TelegramMessagingTool.Tests/TestAssert.cs`
  - `TelegramMessagingTool.Tests/ConfigurationTests.cs`
  - `TelegramMessagingTool.Tests/PluginTests.cs`
  - `TelegramMessagingTool.Tests/CommandTests.cs`
  - `TelegramMessagingTool.Tests/AgentBehaviorEvalTests.cs`
  - `TelegramMessagingTool.Tests/DocumentTests.cs`

**Plan:**

1. Move assertion helpers first with no behavior changes.
2. Move plugin tests second because the sample plugin now has multiple tools.
3. Move configuration tests third.
4. Run helper tests after each move.
5. Keep the console test project dependency-free.

**Commit:**

```bash
git commit -m "Split helper tests by feature area"
```

---

### Task 10.2: Add focused plugin tool tests

**Status:** Completed in current patch (`Extract plugin helper tests`).

**Objective:** Ensure `dotnet_create_project` remains sandboxed and discoverable.

**Files:**
- Modify: `TelegramMessagingTool.Tests/PluginTests.cs`

**Test cases:**

- `PluginToolLoader` loads both `sample_echo` and `dotnet_create_project`.
- `dotnet_create_project` creates only under `GeneratedProjects`.
- Existing non-empty project folder is rejected.
- Unsafe names are rejected.
- `/tools` renders `source: plugin:sample-plugin` and `risk: medium`.

**Commit:**

```bash
git commit -m "Add focused sample plugin tool tests"
```

---

## Priority 11: Operational Diagnostics Commands

### Task 11.1: Add `/health` command

**Objective:** Give Mujahed a single runtime command for checking bot health from Telegram.

**Files:**
- Create: `TelegramMessagingTool/Commands/HealthCommand.cs`
- Modify: `TelegramMessagingTool/Runtime/CommandRouterFactory.cs`
- Modify: `TelegramMessagingTool/Commands/HelpCommand.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs` or `CommandTests.cs`

**Health output should include:**

- Bot process uptime if available
- Database reachable/migrations applied status
- Ollama route summary
- Search enabled/routing mode
- Plugin enabled/count if available
- Document storage root exists
- Import inbox exists
- Warning count from risk config helper

**Do not include:**

- Tokens
- Connection string values
- Raw message content

**Commit:**

```bash
git commit -m "Add health diagnostics command"
```

---

### Task 11.2: Add `/errors [count]` metadata-only command

**Objective:** Show recent runtime errors/warnings without exposing raw payloads.

**Files:**
- Create: `TelegramMessagingTool/Services/RuntimeEventBuffer.cs`
- Create: `TelegramMessagingTool/Commands/ErrorsCommand.cs`
- Modify: `TelegramMessagingTool/Runtime/AppServicesBuilder.cs`
- Modify: `TelegramMessagingTool/Services/RuntimeObservabilityService.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs` or `CommandTests.cs`

**Plan:**

1. Add bounded in-memory ring buffer for runtime events.
2. Store metadata-only entries.
3. `/errors [count]` clamps count to `1..50`, default `10`.
4. Admin-only if possible.

**Commit:**

```bash
git commit -m "Add metadata-only runtime errors command"
```

---

## Priority 12: Media Workflow Polish

### Task 12.1: Add explicit generated-audio send command

**Status:** Partially superseded by current patch — inbound Telegram voice messages can now be saved, transcribed, answered by the agent, and replied to with generated voice/audio when trusted local transcription and TTS providers are configured. `/speaktext` remains storage-only; an explicit `/sendaudio <file-id>` command is still useful for manually sending already-generated sandbox audio.

**Files:**
- Create: `TelegramMessagingTool/Commands/SendVoiceFileCommand.cs` or `SendAudioFileCommand.cs`
- Modify: `TelegramMessagingTool/Runtime/CommandRouterFactory.cs`
- Modify: `TelegramMessagingTool/Commands/HelpCommand.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs` or `CommandTests.cs`

**Command:**

```text
/sendaudio <file-id>
```

**Rules:**

- Only current user's sandboxed audio files.
- Reject non-audio file IDs.
- Verify path is still under `DocumentStorageService.RootDirectory`.
- Do not auto-send TTS from `/speaktext`; user must call `/sendaudio`.

**Commit:**

```bash
git commit -m "Add explicit audio file delivery command"
```

---

### Task 12.2: Add transcript-to-task draft flow

**Objective:** Convert transcribed voice notes into draft task plans without executing changes.

**Files:**
- Create: `TelegramMessagingTool/Commands/TranscriptTaskDraftCommand.cs`
- Modify: `TelegramMessagingTool/Services/TranscriptInsightsService.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs` or `CommandTests.cs`

**Command:**

```text
/transcripttasks <transcript-file-id>
```

**Output:**

- Proposed title
- Bullet task list
- Suggested `/plan ...` command
- No DB task creation in this first patch unless user confirms later

**Commit:**

```bash
git commit -m "Add transcript task draft command"
```

---

## Priority 13: Plugin Safety and Versioning

### Task 13.1: Add plugin API version compatibility checks

**Objective:** Prevent loading old/future plugin manifests against incompatible app contracts.

**Files:**
- Modify: `TelegramMessagingTool/Plugins/PluginManifest.cs`
- Modify: `TelegramMessagingTool/Plugins/PluginManifestScanner.cs`
- Modify: `plugins/SamplePlugin/plugin.json`
- Modify: `plugins/SamplePlugin/plugin.json.example`
- Modify: `docs/plugin-authoring.md`
- Test: `TelegramMessagingTool.Tests/PluginTests.cs`

**Manifest addition:**

```json
"apiVersion": "1.0"
```

**Rules:**

- Missing `apiVersion` should be accepted temporarily with a warning or rejected depending on chosen compatibility mode.
- Prefer warning first to avoid breaking existing local plugins.
- Future incompatible major versions should be rejected.

**Commit:**

```bash
git commit -m "Add plugin API version compatibility checks"
```

---

### Task 13.2: Add plugin command execution risk guidance

**Objective:** The sample `dotnet_create_project` changes state without approval because plugin tools cannot currently plug into the app approval DB. Document and guard this more clearly.

**Files:**
- Modify: `docs/plugin-authoring.md`
- Modify: `README.md`
- Optional modify: `TelegramMessagingTool/Tools/ToolRegistry.cs`

**Plan:**

1. Add docs that state-changing plugin tools run directly today.
2. Recommend medium/high plugin tools be kept sandboxed and reviewed locally.
3. Consider `/tools` highlighting `plugin + can change state` with `⚠` text.
4. Add tests for tool-list warning if rendering changes.

**Commit:**

```bash
git commit -m "Clarify state-changing plugin risk"
```

---

## Priority 14: Optional Machine Configuration Profile Helper

### Task 14.1: Add safe local profile scripts for Mujahed's machine

**Objective:** Make machine-specific all-enabled settings explicit and reversible without changing project defaults.

**Files:**
- Create: `scripts/Set-LocalDevEnvironment.ps1`
- Create: `scripts/Set-SafeEnvironment.ps1`
- Modify: `README.md`

**Rules:**

- Scripts set User env vars only.
- Never write secrets.
- `Set-LocalDevEnvironment.ps1` may set non-secret feature flags to true and `SEARCH_ROUTING_MODE=llm`.
- `Set-SafeEnvironment.ps1` should set risky flags back to safer values:
  - `ALLOW_PUBLIC_ACCESS=false`
  - `LOG_MESSAGE_CONTENT=false`
  - `ENABLE_REPO_WRITE_TOOLS=false`
  - `ENABLE_GITHUB_WRITE_TOOLS=false`

**Commit:**

```bash
git commit -m "Add local environment profile scripts"
```

---

## Recommended Implementation Order

1. **Task 8.1** callback actor authorization.
2. **Task 9.1** `/riskconfig`, because local machine now has many risky flags enabled.
3. **Task 10.1** test suite split before adding more complex tests.
4. **Task 11.1** `/health` diagnostics.
5. **Task 13.1** plugin API version checks.
6. **Task 12.1** explicit audio delivery.
7. Continue remaining tasks in order.

## Risks and Tradeoffs

- Enabling `ALLOW_PUBLIC_ACCESS=true` and `LOG_MESSAGE_CONTENT=true` on the local machine is useful for testing but risky for privacy/security. Project defaults should remain safer.
- Plugin DLLs are trusted OS-level code. `dotnet_create_project` is sandboxed by convention/code checks, not by OS sandboxing.
- LLM search routing can fail safe to no-search if the classifier returns invalid JSON. Keep `/status` and future `/health` clear about current routing mode.
- Splitting tests is low-feature-value but high-maintainability-value before the next several features.

## Definition of Done for the Whole Plan

- Callback buttons cannot be abused by wrong Telegram actors.
- Risky machine config is visible from Telegram and startup logs without leaking secrets.
- Helper tests are split and maintainable.
- `/health` and `/errors` provide operational visibility.
- Media workflows support explicit generated-audio delivery.
- Plugin loading has compatibility/version checks and clearer risk display.
- All changes are tested, released, pushed, restarted, and Telegram-smoke verified.
