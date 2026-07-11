# Next Hardening and Agent Roadmap Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Make TelegramMessagingTool more reliable as a local agent by closing remaining mature-agent risks, improving provider setup UX, and adding safe command/agent observability without broad new risky capabilities.

**Architecture:** Continue roadmap-driven small slices with TDD. Prioritize safety/reliability over feature sprawl: approval concurrency, provider configuration validation, vector-store durability, plugin trust boundaries, document/media limits, then agent UX observability. Each slice must ship with tests, release publish, latest-release restart, Telegram smoke test, and a local commit. GitHub push remains best-effort and should be reported as blocked until credentials are fixed.

**Tech Stack:** C#/.NET 10, Telegram.Bot, EF Core/SQL Server LocalDB, Ollama, local command providers, JSON/local vector store, optional Qdrant, Hermes release workflow.

---

## Current Context

- Latest verified release before this plan: `release/TelegramMessagingTool-20260710-235427`.
- Local branch is ahead of `origin/master`; GitHub push is blocked by missing HTTPS credentials.
- Recently completed:
  - Agent system prompt/tool-boundary hardening.
  - Strict full-response tool-call JSON parsing.
  - Shared local command process support for OCR/STT/TTS.
  - `/providers`, `/ocrimage`, `/imageprompt`, `/voicebrief`, `/voiceplan` and related docs/tests.
- Known remaining high-value areas:
  - Approval race safety.
  - Local vector-store write durability/concurrency.
  - Plugin trust/allowlist hardening.
  - Heavy document/media extraction limits.
  - Local STT/TTS setup and runtime validation UX.
  - Better admin observability for agent/tool/provider behavior.

---

## Roadmap Main Points

| Phase | Priority | Change Set | Outcome |
|---|---:|---|---|
| 1 | P0 | Approval race safety | Prevent double-approval/double-execution of risky actions. |
| 2 | P0 | Provider validation command | Admin can verify OCR/STT/TTS provider config safely before relying on voice/image flows. |
| 3 | P1 | Local vector-store durability | Avoid corrupted/lost vector JSON during concurrent writes. |
| 4 | P1 | Plugin trust hardening | Add hash/allowlist visibility and safer plugin diagnostics. |
| 5 | P1 | Heavy document/media extraction limits | Reduce memory/time risk from large PDFs/DOCX/XLSX/media. |
| 6 | P2 | Agent activity observability | Add compact admin tool/provider/action traces without raw private content. |

---

## Phase 1: Approval Race Safety

**Objective:** Ensure risky pending actions can be approved or denied exactly once, even if duplicate `/approve` commands arrive close together.

**Files likely to change:**
- Modify: `TelegramMessagingTool/Commands/ApproveCommand.cs`
- Modify: `TelegramMessagingTool/Commands/DenyCommand.cs`
- Modify: `TelegramMessagingTool/Data/TelegramDbContext.cs` if concurrency fields are needed
- Modify: `TelegramMessagingTool/Models/PendingAction.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs` or a focused approval test file if one exists

**Steps:**

1. Inspect current `PendingAction` model and approve/deny command flow.
2. Write a failing test that simulates two approval attempts for the same pending action.
3. Add a safe state transition helper, for example `PendingActionTransitionService`, that changes `Pending -> Approved/Denied` only if current status is still pending.
4. Ensure execution happens only after a successful transition.
5. Ensure second approval returns a clear “already decided” message and does not execute side effects again.
6. Add matching deny-after-approve / approve-after-deny tests.
7. Run:
   ```bash
   dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
   dotnet build TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --nologo
   ```
8. Commit:
   ```bash
   git add TelegramMessagingTool TelegramMessagingTool.Tests
   git commit -m "Harden pending action approval transitions"
   ```

**Acceptance criteria:**
- Duplicate approvals do not double-execute.
- Denied actions cannot later execute.
- Reply text is clear for already-decided actions.
- Existing approval commands still work.

---

## Phase 2: Provider Validation Command

**Objective:** Add an admin-only `/providertest` command that safely tests configured OCR/STT/TTS providers with sandboxed sample inputs and secret-safe output.

**Files likely to change:**
- Create: `TelegramMessagingTool/Commands/ProviderTestCommand.cs`
- Modify: `TelegramMessagingTool/Runtime/CommandRouterFactory.cs`
- Modify: `TelegramMessagingTool/Commands/HelpCommand.cs`
- Modify: `TelegramMessagingTool/Services/LocalCommandProcessSupport.cs` only if small test hooks are needed
- Modify: `README.md`
- Test: `TelegramMessagingTool.Tests/DocumentTests.cs` or `ConfigurationTests.cs`

**Command shape:**

```text
/providertest ocr <image-id>
/providertest stt <audio-id>
/providertest tts <short text>
/providertest all
```

**Steps:**

1. Write tests for admin-only access and exact command boundary (`/providertestx` rejected).
2. Write tests for disabled/missing provider output.
3. Write tests with fake local PowerShell providers:
   - OCR returns sample text.
   - STT returns sample transcript.
   - TTS creates a small fake output file.
4. Implement command using existing provider services; do not expose raw command paths or secrets.
5. Return compact readiness and test result lines.
6. Update `/help`, README configuration docs, and `/providers` cross-reference.
7. Run tests/build/security scan.
8. Commit:
   ```bash
   git add TelegramMessagingTool TelegramMessagingTool.Tests README.md
   git commit -m "Add admin provider validation command"
   ```

**Acceptance criteria:**
- Admin can validate local providers without reading secrets.
- Non-admin users are denied.
- Tests cover missing provider, fake success, and boundary rejection.

---

## Phase 3: Local Vector-Store Durability

**Objective:** Make `LocalJsonVectorStore` safer under concurrent use with locking and atomic file replacement.

**Files likely to change:**
- Modify: `TelegramMessagingTool/Services/LocalJsonVectorStore.cs`
- Test: `TelegramMessagingTool.Tests/DocumentTests.cs` or vector-specific test file
- Modify: `README.md` if behavior/limits change

**Steps:**

1. Inspect current local JSON vector store read/write methods.
2. Write a failing test for repeated upserts/deletes that validates the JSON file remains parseable.
3. Add per-file async lock or synchronous lock around read-modify-write.
4. Write to a temp file and atomically replace/move into `VECTOR_STORE_PATH`.
5. Handle corrupted JSON gracefully: return clear error, do not silently erase existing vectors.
6. Add tests for corrupted file handling.
7. Run tests/build.
8. Commit:
   ```bash
   git add TelegramMessagingTool TelegramMessagingTool.Tests README.md
   git commit -m "Harden local vector store persistence"
   ```

**Acceptance criteria:**
- Concurrent vector updates do not corrupt JSON.
- Failed writes do not leave partial files.
- Corruption is reported clearly.

---

## Phase 4: Plugin Trust Hardening

**Objective:** Improve plugin trust visibility and optional hash allowlisting before loading local DLL plugins.

**Files likely to change:**
- Modify: `TelegramMessagingTool/Services/PluginLoader.cs` or equivalent plugin service
- Modify: `TelegramMessagingTool/Commands/PluginsCommand.cs`
- Modify: `TelegramMessagingTool/Models/BotSettings.cs`
- Modify: config parsing in runtime settings builder
- Modify: `README.md`
- Test: `TelegramMessagingTool.Tests/ConfigurationTests.cs` and plugin tests

**Config proposal:**

```text
PLUGIN_ALLOWED_SHA256=hash1,hash2,...
PLUGIN_REQUIRE_HASH_ALLOWLIST=false
```

**Steps:**

1. Add config parsing tests for the two new variables.
2. Add plugin diagnostics tests showing DLL hash without exposing local secrets.
3. Add optional strict mode: if `PLUGIN_REQUIRE_HASH_ALLOWLIST=true`, load only DLLs with SHA256 in allowlist.
4. Keep default compatible: warn when enabled without allowlist, but do not break existing trusted local setup unless strict mode is true.
5. Update `/plugins` output with hash/allowlist state.
6. Update README with warning that in-process plugins are full-trust code execution.
7. Run tests/build/vuln scan.
8. Commit:
   ```bash
   git add TelegramMessagingTool TelegramMessagingTool.Tests README.md
   git commit -m "Add plugin hash allowlist diagnostics"
   ```

**Acceptance criteria:**
- `/plugins` helps the admin verify exactly which DLL is loaded.
- Strict hash allowlist works when enabled.
- Default remains backwards-compatible but warns clearly.

---

## Phase 5: Heavy Document and Media Extraction Limits

**Objective:** Add safer limits around large document/media extraction to avoid memory/time spikes.

**Files likely to change:**
- Modify: `TelegramMessagingTool/Services/DocumentStorageService.cs`
- Modify: PDF/DOCX/XLSX extraction helpers if separate
- Modify: audio/image processing commands if they read full files unnecessarily
- Modify: `TelegramMessagingTool/Models/BotSettings.cs`
- Modify: README config table
- Test: `TelegramMessagingTool.Tests/DocumentTests.cs`

**Config proposal:**

```text
DOCUMENT_EXTRACT_MAX_CHARACTERS=100000
DOCUMENT_EXTRACT_MAX_PAGES=50
DOCUMENT_EXTRACT_TIMEOUT_SECONDS=60
```

**Steps:**

1. Write tests for text truncation limit and clear truncation notice.
2. Add max character cap to extraction paths.
3. Add page/row/sheet limits where practical for PDF/XLSX.
4. Add timeout/cancellation handling around expensive extraction paths.
5. Ensure `/readfile`, `/askfile`, `/indexfile`, and transcript commands surface clear messages when content is truncated.
6. Update `/riskconfig` or `/health` if limits should be visible.
7. Run tests/build.
8. Commit:
   ```bash
   git add TelegramMessagingTool TelegramMessagingTool.Tests README.md
   git commit -m "Add bounded document extraction limits"
   ```

**Acceptance criteria:**
- Large documents are bounded.
- User sees clear truncation/limit notices.
- Existing normal documents still work.

---

## Phase 6: Agent Activity Observability

**Objective:** Add compact admin diagnostics for recent tool/provider/action behavior without raw chat content.

**Command proposal:**

```text
/agentlog [count]
```

**Files likely to change:**
- Create/modify runtime event buffer model if needed
- Create: `TelegramMessagingTool/Commands/AgentLogCommand.cs`
- Modify: `TelegramMessagingTool/Runtime/CommandRouterFactory.cs`
- Modify: `TelegramMessagingTool/Services/SystemLogging.cs` or runtime event capture service
- Modify: `HelpCommand.cs`, `README.md`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Steps:**

1. Write tests for admin-only `/agentlog`, count clamping, empty-log output, and no raw message content.
2. Reuse existing sanitized runtime event buffer where possible.
3. Include recent categories:
   - tool call requested/executed/failed,
   - provider command success/failure/timeout,
   - pending action created/approved/denied/executed,
   - plugin load warnings.
4. Mask user IDs/secrets/paths where needed.
5. Add `/help` and README documentation.
6. Run tests/build/security scan.
7. Commit:
   ```bash
   git add TelegramMessagingTool TelegramMessagingTool.Tests README.md
   git commit -m "Add admin agent activity log command"
   ```

**Acceptance criteria:**
- Admin can inspect recent agent behavior quickly.
- No raw private chat text, tokens, connection strings, or full secret-bearing paths are shown.
- Non-admin access is denied.

---

## Standard Verification for Every Phase

Run after each phase:

```bash
cd /c/temp/TelegramMessagingTool
export PATH="/c/Program Files/dotnet:$PATH"
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
dotnet build TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --nologo
dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
git diff --check
```

Then publish/restart/smoke-test:

```bash
release_dir="release/TelegramMessagingTool-$(date +%Y%m%d-%H%M%S)"
dotnet publish TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --output "$release_dir" --nologo
printf '%s' "$release_dir" > .latest-release
```

Runtime verification:

- Stop only the existing verified `TelegramMessagingTool.exe` process.
- Start from `.latest-release` while copying User environment variables to process scope.
- Confirm `long polling is running`.
- If token/admin chat are available, send a Telegram smoke message.

GitHub handling:

```bash
GIT_ASKPASS=/bin/false GIT_TERMINAL_PROMPT=0 git -c credential.helper= push origin master
```

Expected until credentials are fixed: push may fail with missing username/password. Do not block local release on this; report `ahead N` honestly.

---

## Recommended Execution Order

1. **Start with Phase 1** because approval race safety protects all risky actions.
2. Then **Phase 2** because provider validation makes voice/image setup practical.
3. Then **Phase 3** because vector durability protects document Q&A state.
4. Then **Phase 4** because plugins are full-trust code and need better visibility.
5. Then **Phase 5** for large input reliability.
6. Finish with **Phase 6** for better admin operation/debugging.

---

## Open Questions

- Should `/providertest all` require sample saved image/audio IDs, or should it create tiny synthetic sandbox samples where possible?
- Should plugin hash strict mode default to false for compatibility, or should enabled plugin loading require allowlisted hashes by default?
- Should document extraction limits be global only, or should commands support explicit overrides for admin users?

---

## First Slice to Execute Next

**Phase 1: Approval Race Safety**

Reason: it is the safest/highest-priority next step because it protects every existing risky action: process kill, delete file, repo write, release restart, GitHub writes, and vector clear.
