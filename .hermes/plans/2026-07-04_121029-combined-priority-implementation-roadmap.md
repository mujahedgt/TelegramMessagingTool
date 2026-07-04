# Combined Priority Implementation Roadmap

> **For Hermes:** Use this as the merged master roadmap. Implement one task at a time with TDD, release verification, commit/push, restart, and runtime verification after each completed patch. Do not implement the whole roadmap in one patch.

**Goal:** Combine the original agent-improvement plan, repo-mode coding capability, GitHub integration, plugins, runtime refactor, and safety/observability work into one prioritized implementation order.

**Architecture:** Keep TelegramMessagingTool local-first and approval-backed. Prefer small feature flags, strict JSON schemas, allowlists, and safe pending actions before any risky execution. Preserve the existing structure: `Commands/`, `Tools/`, `Agent/`, `Services/`, `Data/`, and roadmap docs.

**Tech Stack:** C#/.NET 10, Telegram.Bot, EF Core SQL Server LocalDB, Ollama chat/embed APIs, Git/GitHub REST, Windows startup launcher, local release folders, Hermes-assisted TDD workflow.

---

## Current Baseline

Already completed:

- P0 config/search cleanup:
  - `CONVERSATION_MAX_HISTORY`
  - removed hardcoded search typo corrections
  - `SEARCH_ROUTING_MODE=heuristic|off|llm`
- P1 safe command/repo-mode foundation:
  - `git_status`, `git_diff`, `git_log_recent`
  - `run_dotnet_tests`
  - pending-action `publish_release` and `restart_latest_bot` request tools
  - approval-backed `repo_replace_text`
- P2 plugin discovery foundation:
  - manifest parser/scanner
  - `/plugins` read-only inspection
  - plugin docs/template
  - no assembly loading yet
- P3 GitHub read-only foundation:
  - GitHub settings and allowlist
  - `github_repo_info`
  - `github_list_issues`
- Image/voice harness foundations:
  - `/harnesses`, `/images`, `/describeimage`, `/voicefiles`, `/transcribe`
  - feature gates for image vision and audio transcription

---

# Priority 1 — Finish Safe Repo Coding Loop

## Why this is first

The user specifically asked for code-writing/running capability. The bot can now request exact text edits, but the workflow is incomplete without commit/push/release safety. Finish the local coding loop before adding more external integrations.

## P1.1 Add `repo_commit_changes` ✅ Done

**Status:** Implemented with `RepoCommitChangesRequestTool` behind `ENABLE_REPO_WRITE_TOOLS=true`. The tool is admin-only and approval-backed, uses strict JSON `{ "message", "body" }`, creates a high-risk pending action first, and executes only after `/approve`. Execution runs `git diff --check`, refuses empty diffs, validates changed paths through the repo-write allowlist, stages allowed changed paths, commits with fixed `git` arguments, records the commit result in `DecisionNote`, and does not push.

**Objective:** Allow the bot to request a safe commit after approved edits and passing tests.

**Files likely to change:**

- Modify: `TelegramMessagingTool/BotRuntime.cs`
- Modify: `TelegramMessagingTool/Tools/ToolRegistryFactory.cs`
- Create/modify: `TelegramMessagingTool/Tools/CommandExecution/RepoWriteRequestTools.cs`
- Modify: `TelegramMessagingTool/Services/PendingActionExecutor.cs`
- Modify: `TelegramMessagingTool.Tests/Program.cs`
- Modify: `README.md`
- Modify: `.hermes/plans/2026-07-04_121029-combined-priority-implementation-roadmap.md`

**Design:**

Add tool:

```text
repo_commit_changes
```

Strict JSON input:

```json
{"message":"Add repo text replacement tool","body":"Adds approval-backed code edit support with tests."}
```

Rules:

- Behind `ENABLE_REPO_WRITE_TOOLS=true`.
- Requires admin and pending-action context.
- Creates pending action first.
- On approval:
  - run `git diff --check`
  - reject empty diff
  - optionally reject obvious secret patterns in staged diff
  - stage only allowlisted project files, not releases/secrets/user files
  - commit with message/body
- Do not push.

**Tests:**

- Disabled by default.
- Registered only when repo write tools enabled and pending service exists.
- Requires approval.
- Non-admin cannot create pending action.
- Empty/invalid message rejected.
- Approval commits when diff exists.
- Approval refuses empty diff.
- Approval records execution result in `DecisionNote`.

**Verification commands:**

```bash
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
```

## P1.2 Add `repo_push_changes` ✅ Done

**Status:** Implemented with `RepoPushChangesRequestTool` behind `ENABLE_REPO_WRITE_TOOLS=true`. The tool is admin-only and approval-backed, creates a high-risk pending action first, refuses dirty working trees during execution, detects the current named branch, and runs fixed non-interactive `git push origin <current-branch>` only after `/approve`. It does not support force push or user-supplied push arguments.

**Objective:** Allow approved push to GitHub only after a local commit exists.

**Rules:**

- High-risk approval-backed action.
- No force push.
- Push only current branch to configured `origin`.
- Use existing Git Credential Manager non-interactive handling.
- Refuse if working tree has uncommitted changes unless explicitly allowed later.

**Tests:**

- Creates pending action only.
- Non-admin rejected.
- Executor uses fixed `git push origin <branch>` with no shell/user args.
- Refuses dirty tree.

## P1.3 Execute `publish_release` after approval ✅ Done

**Status:** Implemented approved execution for `publish_release`. It runs fixed `dotnet publish` for `TelegramMessagingTool/TelegramMessagingTool.csproj`, writes to a timestamped `release/TelegramMessagingTool-yyyyMMdd-HHmmss` folder, updates `.latest-release` only after publish success, records the release path in `DecisionNote`, and does not restart the bot.

**Objective:** Make existing `publish_release` pending action actually publish a timestamped release after approval.

**Rules:**

- Run fixed `dotnet publish` only.
- Update `.latest-release` only after publish success.
- Do not restart in same action.
- Record release path in `DecisionNote`.

## P1.4 Execute `restart_latest_bot` after approval ✅ Done

**Status:** Implemented approved execution for `restart_latest_bot`. It validates `.latest-release`, writes a fixed restart script under `release/`, hands off runtime environment variables including `ENABLE_REPO_WRITE_TOOLS`, stops old `TelegramMessagingTool` processes from the detached script, and starts the latest release with the project root as working directory.

**Objective:** Make existing `restart_latest_bot` pending action safely restart the newest `.latest-release`.

**Rules:**

- Stop old `TelegramMessagingTool.exe` processes.
- Start `.latest-release` using environment handoff.
- Include `ENABLE_REPO_WRITE_TOOLS` in runtime env handoff.
- Verify process path and long-polling startup.
- Keep Telegram API send test as separate verification when token/admin chat are available.

## P1.5 Add safer patch capability later ✅ Done

**Status:** Implemented `repo_apply_patch` as an admin-only, approval-backed unified-diff tool. It creates a pending action first, extracts affected paths from diff headers, rejects binary/generated/runtime/out-of-root paths, runs `git apply --check`, and only applies the patch after `/approve`.

**Objective:** Move beyond exact text replacement without exposing arbitrary filesystem writes.

Possible tool:

```text
repo_apply_patch
```

Rules:

- Unified diff or structured patch only.
- Must stay under project root.
- Must reject binary/generated/runtime folders.
- Must show preview and require approval.
- Implement only after `repo_commit_changes` and `repo_push_changes` are stable.

---

# Priority 2 — Complete GitHub Integration

## Why second

GitHub tools are useful for workflow and portfolio, but local repo safety should come first because GitHub write tools affect remote state.

## P2.1 Add `github_get_issue` ✅ Done

**Status:** Implemented `GitHubGetIssueTool` behind `ENABLE_GITHUB_TOOLS=true`. It is read-only, uses the default repo when owner/repo are omitted, rejects repositories outside `GITHUB_ALLOWED_REPOS`, requires a positive issue number, rejects pull-request JSON as not an issue, sends optional bearer auth without rendering the token, and returns issue number/title/state, author, labels, assignees, timestamps, URL, and a bounded body excerpt.

**Objective:** Read a single issue with comments count/basic metadata.

**Tool:**

```text
github_get_issue
```

Input:

```json
{"owner":"mujahedgt","repo":"TelegramMessagingTool","number":42}
```

Rules:

- Read-only.
- Must use `GITHUB_ALLOWED_REPOS`.
- Exclude token from output.

## P2.2 Add `github_list_prs` ✅ Done

**Status:** Implemented `GitHubListPullRequestsTool` as read-only `github_list_prs` behind `ENABLE_GITHUB_TOOLS=true`. It uses the configured default repo when owner/repo are omitted, rejects repos outside `GITHUB_ALLOWED_REPOS`, supports `state=open|closed|all`, clamps `limit` to `1..50`, calls `/repos/{owner}/{repo}/pulls`, never renders `GITHUB_TOKEN`, and returns PR number/title/state, author, head/base branches, draft/ready status, timestamps, and URL.

**Objective:** List pull requests for allowed repos.

Input:

```json
{"owner":"mujahedgt","repo":"TelegramMessagingTool","state":"open","limit":10}
```

## P2.3 Add `github_get_pr_status`

**Objective:** Read PR checks/review/status summary.

## P2.4 Add `github_create_issue`

**Objective:** Let the bot request issue creation safely.

Rules:

- Add `ENABLE_GITHUB_WRITE_TOOLS=false`.
- Admin-only or configurable admin-only by default.
- Strict JSON input: owner, repo, title, body, labels.
- Repo allowlist mandatory.
- Create pending action first.
- On approval, call GitHub API.

## P2.5 Add `github_comment_issue`

**Objective:** Approval-backed issue comment tool.

## P2.6 Defer high-risk GitHub write features

Later only:

- `github_create_branch`
- `github_commit_file`
- `github_open_pr`
- `github_merge_pr`

These should wait until repo-mode local commit/push and approval audit are mature.

---

# Priority 3 — Runtime Composition Refactor

## Why third

`Program.cs` is large. More tools will become painful unless startup/composition is cleaner.

## P3.1 Extract command-router construction

**Files:**

- Create: `TelegramMessagingTool/Runtime/CommandRouterFactory.cs`
- Modify: `TelegramMessagingTool/Program.cs`
- Modify: `TelegramMessagingTool.Tests/Program.cs`

**Goal:** Move command construction out of `Program.cs` while keeping behavior unchanged.

## P3.2 Extract tool-registry/runtime services construction

**Files:**

- Create: `TelegramMessagingTool/Runtime/AppServices.cs`
- Create: `TelegramMessagingTool/Runtime/AppServicesBuilder.cs`
- Modify: `Program.cs`

## P3.3 Extract Telegram message/update handling

**Files:**

- Create: `TelegramMessagingTool/Runtime/TelegramUpdateHandler.cs`
- Modify: `Program.cs`

## P3.4 Extract console input handling

**Files:**

- Create: `TelegramMessagingTool/Runtime/ConsoleInputHandler.cs`
- Modify: `Program.cs`

## P3.5 Keep `Program.cs` thin

Target responsibilities:

- load settings
- build services
- start Telegram polling
- start console loop
- handle shutdown

---

# Priority 4 — Plugin Loading, Carefully

## Why fourth

Plugin manifest inspection is done, but assembly loading is trusted code execution. Do this only after repo/runtime safety improves.

## P4.1 Create abstraction package

**Files:**

- Create: `TelegramMessagingTool.Abstractions/TelegramMessagingTool.Abstractions.csproj`
- Move/share:
  - `IAgentTool`
  - `ToolResult`

## P4.2 Add trusted plugin loader

Rules:

- Keep `ENABLE_PLUGINS=false` default.
- Load only from `PLUGIN_DIRECTORY`.
- Only load plugin manifests that pass scanner validation.
- Show plugin source in `/tools`.
- Reject duplicate tool names.
- Document that plugin DLLs are trusted OS-level code.

## P4.3 Add plugin risk metadata

Before loading risky plugin tools, add risk/approval metadata.

---

# Priority 5 — Tool Safety and Approval Improvements

## P5.1 Add structured risk metadata to tools

Current `IAgentTool` only has:

```csharp
bool RequiresApproval
```

Add later:

```csharp
ToolRiskLevel RiskLevel
string SafetySummary
bool IsReadOnly
```

## P5.2 Improve `/pending` and `/action` previews

Add richer previews:

- file path
- diff summary for repo edits
- git command preview for commit/push
- GitHub repo/issue preview
- exact risk level

## P5.3 Add secret/danger scanning

Before commit/push/release:

- scan diffs for token-like patterns
- reject `.env`, secret files, local DB files, release outputs
- warn on suspicious binary/generated files

## P5.4 Add audit/export

Useful command later:

```text
/actions [count]
```

Shows recent action audit records.

---

# Priority 6 — Image and Voice Agents

## Why later

The harness foundation is done, but real image/voice execution needs provider safety and resource planning.

## P6.1 Improve image agent

Next possible tasks:

- Better `/describeimage` prompt/config options.
- Optional OCR provider behind `ENABLE_IMAGE_OCR=false`.
- Image generation only after storage/approval rules are clear.

## P6.2 Improve voice agent

Next possible tasks:

- Add local Whisper/provider integration behind `ENABLE_AUDIO_TRANSCRIPTION=true`.
- Store transcripts as sandboxed text documents.
- Add summarization/task extraction using `OLLAMA_MODEL_VOICE`.
- TTS later, with explicit output storage and user approval for sending audio.

---

# Priority 7 — Observability, Evals, and Docs

## P7.1 Runtime observability

Add clearer logs for:

- tool calls
- pending action creation
- approval execution
- repo write results
- GitHub API failures

Keep message content logging disabled by default.

## P7.2 Agent behavior evals

Add scripted tests for:

- model emits tool_call JSON
- approval tools create pending actions
- failed tool results are explained safely
- search routing avoids false positives

## P7.3 Documentation cleanup

Keep README, `/help`, `/status`, `/tools`, and roadmap docs synchronized after every feature.

---

# Recommended Implementation Order

Use this exact order unless the user changes priorities:

1. `repo_commit_changes`
2. `repo_push_changes`
3. execute approved `publish_release`
4. execute approved `restart_latest_bot`
5. `github_get_issue`
6. `github_list_prs`
7. `github_get_pr_status`
8. `github_create_issue`
9. extract `CommandRouterFactory`
10. extract `AppServicesBuilder`
11. extract `TelegramUpdateHandler`
12. extract `ConsoleInputHandler`
13. plugin abstraction package
14. trusted plugin loading
15. structured tool risk metadata
16. richer pending action previews
17. secret/danger diff scanning
18. image OCR/vision improvements
19. voice transcription provider
20. observability/evals/docs polish

---

# Per-Patch Completion Checklist

Every implementation patch should finish with:

```bash
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
release_dir="release/TelegramMessagingTool-$(date +%Y%m%d-%H%M%S)"
dotnet publish TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --output "$release_dir" --nologo
printf '%s' "$release_dir" > .latest-release
git diff --check
git add <changed-files>
git commit -m "Clear commit message"
git push origin master
```

Then restart and verify:

- only one `TelegramMessagingTool.exe` process
- process path matches `.latest-release`
- long polling startup confirms running
- Telegram API send test if token/admin chat are available

---

# Immediate Next Task

Start with:

```text
Priority 1.1 — Add repo_commit_changes
```

This is the most valuable next step because it completes the local repo coding loop after `repo_replace_text` and `run_dotnet_tests`.
