# Advanced Agent Improvement Plan

> **For Hermes:** Use subagent-driven-development or normal TDD implementation task-by-task. Do not implement the whole roadmap in one patch.

**Goal:** Make TelegramMessagingTool more powerful, safer, more configurable, and easier to extend without losing the current local-first safety model.

**Architecture:** Keep the current layered design: Telegram commands/callbacks in `Commands/` and `Telegram/`, agent orchestration in `Agent/`, tools in `Tools/`, durable state in EF Core models/services, and risky execution behind `PendingActionService` + `PendingActionExecutor`. Add new capabilities as feature-flagged services with tests first.

**Tech Stack:** C#/.NET 10, Telegram.Bot, EF Core SQL Server LocalDB, Ollama chat/embed APIs, optional plugin assemblies loaded from `plugins/`, optional GitHub integration through REST/Octokit/`gh`, existing approval workflow for risky actions.

---

## Current Project Review

The project is already in a strong state:

- Telegram bot and local console share the same command router and agent runner.
- SQL Server LocalDB persists users, messages, memories, uploaded files, document chunks, pending actions, and task plans.
- The agent has bounded safe tool calling via `IAgentTool`, `ToolRegistry`, and `AgentRunner`.
- Risky actions have a database-backed approval path via `/pending`, `/action`, `/approve`, `/deny`.
- File/document handling is sandboxed through `DocumentStorageService`.
- Online search exists behind `ENABLE_ONLINE_SEARCH`.
- Inline task buttons now support open, done-step, done-all, and cancel callbacks safely through `TaskCallbackService`.
- The current tests are dependency-light and fast, making TDD practical.

Main improvement themes:

1. Replace hardcoded heuristics/config with typed settings and classifier services.
2. Make the tool system extensible through plugins, but keep strict safety gates.
3. Add controlled execution capabilities, not arbitrary shell access.
4. Add GitHub as a first-class workflow integration.
5. Improve maintainability by extracting large `Program.cs` wiring into runtime composition services.

---

# Recommended Priority Order

| Priority | Area | Why |
|---:|---|---|
| P0 | Config cleanup + search typo/routing cleanup | Low risk, removes magic numbers and brittle behavior. |
| P1 | Safe model-generated command execution | High value, but must be approval-gated and allowlisted. |
| P2 | Plugin discovery from `plugins/` | Unlocks extensibility without recompiling the main app. |
| P3 | GitHub integration | Major productivity feature for the user’s dev workflow. |
| P4 | Runtime composition refactor | Makes the project easier to maintain as features grow. |
| P5 | Observability, evals, and docs | Makes the bot safer to operate and easier to debug. |

---

# Phase 0: Configuration and Routing Cleanup

## Goal

Remove hardcoded behavior that will become painful as the agent grows.

## 0.1 Move conversation history window into `BotSettings` ✅ Done

**Status:** Implemented in commit `5ca5395` with `CONVERSATION_MAX_HISTORY`, default `8`, clamp `1..50`, tests, README docs, publish, push, and runtime verification.

**Problem:** `Program.cs` hardcoded `maxHistory: 8` in both console and Telegram paths.

**Files:**

- Modify: `TelegramMessagingTool/BotRuntime.cs`
- Modify: `TelegramMessagingTool/Program.cs:317-322`
- Modify: `TelegramMessagingTool/Program.cs:524-530`
- Modify: `TelegramMessagingTool.Tests/Program.cs`
- Modify: `README.md`

**Design:**

Add to `BotSettings`:

```csharp
int ConversationMaxHistory
```

Environment variable:

```text
CONVERSATION_MAX_HISTORY=8
```

Suggested rules:

- default: `8`
- minimum: `1`
- maximum: `50`
- invalid value falls back to default

**Implementation steps:**

1. Add `ConversationMaxHistory` to `BotSettings` primary constructor.
2. Add `BotConfiguration.ParseClampedInt(...)` helper.
3. Load `CONVERSATION_MAX_HISTORY` in `LoadFromEnvironment()`.
4. Replace both `maxHistory: 8` call sites with `settings.ConversationMaxHistory`.
5. Add tests for default, valid override, low clamp, high clamp, and invalid fallback.
6. Document the variable in README.

**Verification:**

```bash
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
```

---

## 0.2 Generalize or remove Mitsubishi/Lancer typo correction ✅ Done

**Status:** Implemented by preserving the user's whitespace-normalized query, removing `CorrectCommonSearchTypos(...)`, removing the hardcoded Mitsubishi/Lancer instruction example, keeping generic model-side correction guidance, and adding tests/README docs.

**Problem:** `OnlineSearchTool.CorrectCommonSearchTypos(...)` contained vehicle-specific corrections:

```csharp
Mitsubateie -> Mitsubishi
Lanser -> Lancer
```

This was useful for one case but is not general enough for a reusable bot.

**Files:**

- Modify: `TelegramMessagingTool/Tools/OnlineSearchTool.cs:100-143`
- Modify: `TelegramMessagingTool/Tools/OnlineSearchTool.cs:310-318`
- Modify: `TelegramMessagingTool/Tools/ToolRegistry.cs:52-57`
- Modify: `TelegramMessagingTool.Tests/Program.cs`
- Modify: `README.md:140-148`

**Recommended approach:** remove domain-specific correction from `OnlineSearchTool` and move correction responsibility to the LLM query-generation step.

Why:

- hardcoded typo maps scale badly
- they introduce surprising corrections for non-car queries
- the LLM is better at correcting obvious misspellings in context

**Safer replacement:**

Create a small reusable service later if needed:

```csharp
public interface ISearchQueryNormalizer
{
    SearchQueryNormalizationResult Normalize(string userText);
}

public sealed record SearchQueryNormalizationResult(
    string Query,
    string? CorrectionNote,
    bool WasChanged);
```

Start with only whitespace cleanup and query length clamp. Do not add fuzzy correction until there is a real generic dictionary/provider.

**Implementation steps:**

1. Add tests proving `BuildSearchQueryVariants("Mitsubateie Lanser 1992")` does not force Mitsubishi/Lancer-specific corrections anymore.
2. Remove `CorrectCommonSearchTypos(...)` or change it to a no-op whitespace normalizer.
3. Remove Mitsubishi-specific examples from `ToolRegistry.RenderToolInstructions()`.
4. Update README search behavior notes.

**Verification:**

```bash
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
```

---

## 0.3 Replace keyword-based direct search heuristics with a lightweight classifier — foundation ✅ Done

**Status:** Implemented by extracting the existing direct-search behavior into `ISearchRoutingClassifier`, `SearchRoutingDecision`, and `HeuristicSearchRoutingClassifier`; adding `OffSearchRoutingClassifier`; wiring `SEARCH_ROUTING_MODE=heuristic|off|llm`; and adding `LlmSearchRoutingClassifier` with strict JSON parsing and safe no-search fallback. `AgentRunner` now accepts an injectable classifier while preserving the old `TryBuildDirectSearchQuery(...)` compatibility helper.

**Problem:** `AgentRunner.TryBuildDirectSearchQuery(...)` triggered web search if words like `price`, `today`, `released`, or `2026` appeared anywhere in the message.

This can cause false positives:

- “What is the price field in this database?”
- “Today I learned C# delegates; explain them.”
- “Released means what in software branching?”

**Files:**

- Modify: `TelegramMessagingTool/Agent/AgentRunner.cs:22-30`
- Modify: `TelegramMessagingTool/Agent/AgentRunner.cs:133-167`
- Create: `TelegramMessagingTool/Agent/SearchRoutingClassifier.cs`
- Modify: `TelegramMessagingTool.Tests/Program.cs`

**Recommended staged design:**

Introduce:

```csharp
public interface ISearchRoutingClassifier
{
    Task<SearchRoutingDecision> ClassifyAsync(
        IReadOnlyList<OllamaMessageDto> conversationContext,
        CancellationToken cancellationToken);
}

public sealed record SearchRoutingDecision(
    bool ShouldSearch,
    string Query,
    string Reason,
    double Confidence);
```

Implement two classifiers:

1. `HeuristicSearchRoutingClassifier` — current behavior, but isolated and testable.
2. `LlmSearchRoutingClassifier` — asks the model for strict JSON.

Strict JSON shape:

```json
{
  "should_search": true,
  "query": "clean search query",
  "reason": "needs current external facts",
  "confidence": 0.82
}
```

Prompt rules:

- Search only if the answer needs current/external facts.
- Do not search for local project questions, coding explanations, definitions, or personal conversation.
- Return `should_search=false` for internal app/database/project questions even if they contain words like `price`, `today`, `latest`, or `release`.
- Use only the latest user message plus short context.

**Feature flag:**

```text
SEARCH_ROUTING_MODE=heuristic|llm|off
```

Default initially: `heuristic` for backward compatibility. After tests: switch default to `llm` if stable.

**Implementation tasks:**

1. ✅ Extract current `TryBuildDirectSearchQuery` into `HeuristicSearchRoutingClassifier` unchanged.
2. ✅ Add tests for current behavior before/refactor coverage.
3. ✅ Change `AgentRunner` constructor to accept `ISearchRoutingClassifier`.
4. ✅ Wire `SEARCH_ROUTING_MODE=heuristic|off` from `BotSettings.SearchRoutingMode` and add `OffSearchRoutingClassifier`.
5. ✅ Add `LlmSearchRoutingClassifier` with strict JSON parsing and safe fallback to no-search on parse failure.
6. ✅ Add tests using fake chat client responses:
   - current/latest external facts → search
   - invalid classifier JSON → no search
   - mode factory creates `llm`, `heuristic`, and `off`
7. ✅ Wire `llm` mode from `BotSettings.SearchRoutingMode`.
8. ✅ Update README.

---

# Phase 1: Safe Model-Generated Command Execution — foundation started ✅

## Goal

Allow the model to propose safe local actions, but execute only predefined allowlisted commands/tools through the existing approval model.

## Do not build arbitrary shell yet

Avoid giving the LLM raw shell access. Instead, build command tools with strict schemas.

Examples of safe-ish command tools:

| Tool | Risk | Execution model |
|---|---|---|
| `list_directory` | low | sandboxed root, read-only |
| `read_project_file` | low | file size/type limits |
| `run_dotnet_tests` | medium | fixed command templates only |
| `git_status` | low | read-only |
| `git_diff` | low | read-only |
| `create_task_plan` | low | DB mutation but user-owned |
| `publish_release` | high | admin approval required |
| `restart_bot` | high | admin approval required |

## Files likely to change

- Create: `TelegramMessagingTool/Tools/CommandExecution/CommandSpec.cs`
- Create: `TelegramMessagingTool/Tools/CommandExecution/SafeCommandExecutor.cs`
- Create: `TelegramMessagingTool/Tools/CommandExecution/RunDotnetTestsTool.cs`
- Create: `TelegramMessagingTool/Tools/CommandExecution/GitStatusTool.cs`
- Modify: `TelegramMessagingTool/Tools/ToolRegistryFactory.cs`
- Modify: `TelegramMessagingTool/Services/PendingActionExecutor.cs`
- Modify: `TelegramMessagingTool.Tests/Program.cs`
- Modify: `README.md`

## Recommended design

```csharp
public sealed record CommandSpec(
    string Name,
    string Executable,
    IReadOnlyList<string> FixedArguments,
    string WorkingDirectory,
    TimeSpan Timeout,
    bool RequiresApproval,
    IReadOnlySet<int> AllowedExitCodes);
```

Rules:

- no free-form shell strings
- no `cmd.exe /c`, `powershell -Command`, `bash -c` in user-controlled input
- fixed executable + argument list only
- working directory must be under configured project root
- timeout required
- stdout/stderr max length required
- high-risk commands create pending actions instead of executing immediately

Suggested config:

```text
ENABLE_SAFE_COMMAND_TOOLS=false
SAFE_COMMAND_PROJECT_ROOT=C:\temp\TelegramMessagingTool
SAFE_COMMAND_TIMEOUT_SECONDS=120
SAFE_COMMAND_OUTPUT_MAX_CHARS=12000
```

## Bite-sized tasks

### Task 1.1 Add command spec and executor — partial foundation ✅

**Status:** Added a conservative first implementation for read-only Git tools using fixed `git` executable/arguments, configured project root, timeout, output truncation, and no shell wrapper. A fuller reusable `CommandSpec`/`SafeCommandExecutor` abstraction can still be extracted before medium-risk tools.

Tests:

- rejects missing executable
- rejects working directory outside root
- clamps timeout
- truncates output
- returns exit code and output

### Task 1.2 Add read-only Git tools ✅ Done

**Status:** Added `ENABLE_SAFE_COMMAND_TOOLS=false` default, `SAFE_COMMAND_PROJECT_ROOT`, and optional read-only Git tools in `ToolRegistryFactory`. The tools are disabled by default and require no approval because they only inspect repository state.

Tools:

- `git_status`
- `git_diff`
- `git_log_recent`

All should be `RequiresApproval=false` because they are read-only.

### Task 1.3 Add fixed test runner tool

Tool:

```text
run_dotnet_tests
```

Input should be limited to a known enum:

```json
{"target":"helper-tests"}
```

Not arbitrary command text.

### Task 1.4 Add approval-gated release/restart tools later

Tools:

- `publish_release`
- `restart_latest_bot`

These must create pending actions and require admin approval.

---

# Phase 2: Plugin Discovery from `plugins/`

## Goal

Automatically register external `IAgentTool` implementations from a `plugins/` folder without recompiling the main app.

## Current state

`ToolRegistryFactory.Create(...)` hardcodes:

```csharp
new DateTimeTool()
new CalculatorTool()
new BotStatusTool(settings)
new OnlineSearchTool(searchClient)
```

## Proposed plugin architecture

### Plugin contract package

Best long-term design: split tool contracts into a small shared project/package.

Create:

```text
TelegramMessagingTool.Abstractions/
```

Move or duplicate stable interfaces:

```csharp
public interface IAgentTool
public sealed record ToolResult
```

Then both main app and plugins reference the same abstraction assembly.

### Plugin manifest

Each plugin folder:

```text
plugins/
  MyPlugin/
    MyPlugin.dll
    plugin.json
```

`plugin.json`:

```json
{
  "id": "my-plugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "entryAssembly": "MyPlugin.dll",
  "enabled": true,
  "riskLevel": "low",
  "allowedToolNames": ["my_tool"]
}
```

## Files likely to change

- Create: `TelegramMessagingTool.Abstractions/TelegramMessagingTool.Abstractions.csproj`
- Move/duplicate: `IAgentTool`, `ToolResult`
- Create: `TelegramMessagingTool/Plugins/PluginManifest.cs`
- Create: `TelegramMessagingTool/Plugins/PluginLoader.cs`
- Modify: `TelegramMessagingTool/Tools/ToolRegistryFactory.cs`
- Modify: `TelegramMessagingTool/BotRuntime.cs`
- Modify: `TelegramMessagingTool.Tests/Program.cs`
- Modify: `README.md`

## Safety requirements

- Plugins disabled by default:

```text
ENABLE_PLUGINS=false
PLUGIN_DIRECTORY=plugins
```

- Load only from configured plugin directory.
- Ignore plugins without `plugin.json`.
- Require tool names to match `^[a-z][a-z0-9_]{1,40}$`.
- Prevent duplicate tool names.
- Do not allow plugin overwrite of built-in tools unless `ALLOW_PLUGIN_TOOL_OVERRIDE=true`.
- Treat plugin tools as untrusted from a UX perspective: show source plugin in `/tools`.
- Consider plugin assembly loading as fully trusted code at the OS level; document that only trusted plugins should be installed.

## Bite-sized tasks

1. Add `PluginManifest` parser with tests.
2. Add tool-name validation with tests.
3. Add duplicate handling tests.
4. Add `PluginLoader` that scans but does not execute tools yet.
5. Add test plugin fixture under `TelegramMessagingTool.Tests/TestPlugins/`.
6. Register plugin tools only when `ENABLE_PLUGINS=true`.
7. Show plugin source in `/tools` or a new `/plugins` command.
8. Document plugin authoring.

---

# Phase 3: GitHub Agent Integration

## Goal

Connect the agent to GitHub for repository inspection, issue creation, PR status checks, and eventually safe branch/commit workflows.

## Recommended feature flag

```text
ENABLE_GITHUB_TOOLS=false
GITHUB_TOKEN=...
GITHUB_DEFAULT_OWNER=mujahedgt
GITHUB_DEFAULT_REPO=TelegramMessagingTool
GITHUB_ALLOWED_REPOS=mujahedgt/TelegramMessagingTool,mujahedgt/IsolationForestServer
```

Never log `GITHUB_TOKEN`.

## Tool set

Start read-only:

| Tool | Approval | Purpose |
|---|---:|---|
| `github_repo_info` | no | repo metadata |
| `github_list_issues` | no | list open issues |
| `github_get_issue` | no | read issue details |
| `github_list_prs` | no | list PRs |
| `github_get_pr_status` | no | PR checks/review state |
| `github_search_code` | no | search allowed repos |

Then low-risk writes:

| Tool | Approval | Purpose |
|---|---:|---|
| `github_create_issue` | optional/admin configurable | create issue |
| `github_comment_issue` | optional/admin configurable | add comment |

Defer high-risk:

| Tool | Approval | Purpose |
|---|---:|---|
| `github_create_branch` | yes | branch changes |
| `github_commit_file` | yes | commit file changes |
| `github_open_pr` | yes | create PR |
| `github_merge_pr` | yes + explicit admin | merge PR |

## Files likely to change

- Create: `TelegramMessagingTool/Tools/GitHub/GitHubClient.cs`
- Create: `TelegramMessagingTool/Tools/GitHub/GitHubSettings.cs`
- Create: `TelegramMessagingTool/Tools/GitHub/GitHubRepoPolicy.cs`
- Create: `TelegramMessagingTool/Tools/GitHub/GitHubRepoInfoTool.cs`
- Create: `TelegramMessagingTool/Tools/GitHub/GitHubListIssuesTool.cs`
- Create: `TelegramMessagingTool/Tools/GitHub/GitHubCreateIssueTool.cs`
- Modify: `TelegramMessagingTool/Tools/ToolRegistryFactory.cs`
- Modify: `TelegramMessagingTool/BotRuntime.cs`
- Modify: `TelegramMessagingTool.Tests/Program.cs`
- Modify: `README.md`

## Safety requirements

- Repo allowlist is mandatory for write tools.
- Token must never appear in `/status`, logs, or tool output.
- Write tools should include a dry-run preview.
- Issue/PR creation should require structured JSON input, not raw prompt text.
- High-risk repo changes must use pending approval flow.

## Suggested first implementation

1. Add GitHub settings parsing.
2. Add `GitHubRepoPolicy` with allowlist tests.
3. Add read-only `github_repo_info` using REST API.
4. Add read-only `github_list_issues`.
5. Add `github_create_issue` behind `ENABLE_GITHUB_WRITE_TOOLS=false` initially.
6. Add `/github` command later for quick status and config summary.

---

# Phase 4: Runtime Composition Refactor

## Goal

Shrink `Program.cs` and make startup easier to test.

## Current issue

`Program.cs` currently handles:

- settings loading
- HttpClient setup
- service construction
- command registration
- Telegram long-polling
- console input
- message handling
- callback handling
- document handling
- reminder loop

This is workable now but will become hard to maintain with plugins, GitHub, and safe commands.

## Proposed files

- Create: `TelegramMessagingTool/Runtime/AppServices.cs`
- Create: `TelegramMessagingTool/Runtime/AppServicesBuilder.cs`
- Create: `TelegramMessagingTool/Runtime/TelegramUpdateHandler.cs`
- Create: `TelegramMessagingTool/Runtime/ConsoleInputHandler.cs`
- Create: `TelegramMessagingTool/Runtime/CommandRouterFactory.cs`
- Create: `TelegramMessagingTool/Runtime/HttpClientFactory.cs`
- Modify: `TelegramMessagingTool/Program.cs`

## Staged approach

1. Extract command-router construction only.
2. Extract tool-registry construction.
3. Extract message handling into `TelegramUpdateHandler`.
4. Extract console handling.
5. Leave `Program.cs` as a thin composition root.

Do this after the feature work above or between phases if `Program.cs` becomes a bottleneck.

---

# Phase 5: Tool Safety and Approval Improvements

## Improvements

### 5.1 Add structured risk metadata to `IAgentTool`

Current:

```csharp
bool RequiresApproval { get; }
```

Better:

```csharp
ToolRiskLevel RiskLevel { get; }
ToolExecutionScope Scope { get; }
```

Example:

```csharp
public enum ToolRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}
```

This lets `/tools`, prompts, and approval flow explain risk better.

### 5.2 Add tool input schemas

Add optional schema/usage docs:

```csharp
string InputSchemaJson { get; }
string ExampleInputJson { get; }
```

This helps the model call tools correctly and helps plugin authors.

### 5.3 Add rate limits per user/tool

Suggested table:

```text
ToolExecutionLog
- Id
- ConnectedUserId
- ToolName
- Success
- CreatedAt
- DurationMs
- InputHash
- OutputLength
```

Rate limit examples:

- online search: 10/min/user
- GitHub reads: 30/min/user
- command tools: 5/min/admin

---

# Phase 6: Smarter Search and Retrieval

Beyond the classifier and typo cleanup:

## 6.1 Better search provider abstraction

Create:

```csharp
public interface ISearchProvider
{
    string Name { get; }
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken);
}
```

Providers:

- DuckDuckGo Lite
- Startpage
- Mojeek
- future: Brave Search API, Tavily, SerpAPI, SearXNG

## 6.2 Cache search results

Avoid repeated public search calls:

```text
SearchCache
- QueryHash
- Provider
- ResultJson
- CreatedAtUtc
- ExpiresAtUtc
```

Default TTL: 15 minutes.

## 6.3 Query planning prompt

Before search, ask model for:

```json
{
  "should_search": true,
  "queries": ["...", "..."],
  "must_include": ["official", "docs"],
  "avoid": ["ads", "spam"]
}
```

Limit to 1 query initially.

---

# Phase 7: Documentation and Test Improvements

## 7.1 Split the large test file

Current tests are in one large `TelegramMessagingTool.Tests/Program.cs`.

Suggested split later:

```text
TelegramMessagingTool.Tests/
  BotConfigurationTests.cs
  SearchToolTests.cs
  AgentRunnerTests.cs
  TaskCallbackTests.cs
  PendingActionTests.cs
  DocumentTests.cs
```

If the project wants to stay dependency-free, keep the console test runner but split into static classes.

## 7.2 Add architecture docs

Create:

```text
docs/architecture.md
docs/tool-safety.md
docs/plugin-authoring.md
docs/github-tools.md
```

## 7.3 Add smoke-test script

Create:

```text
scripts/verify-release.ps1
scripts/verify-release.sh
```

The script should run:

```bash
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
dotnet ef migrations has-pending-model-changes --project TelegramMessagingTool/TelegramMessagingTool.csproj --startup-project TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --no-build
dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
```

---

# Suggested Immediate Next Sprint

If implementing next, do this order:

1. `CONVERSATION_MAX_HISTORY` setting. **Status: complete** — `BotSettings.ConversationMaxHistory` is loaded from `CONVERSATION_MAX_HISTORY`, clamped to `1..50`, and used by both Telegram and console conversation context paths.
2. Remove Mitsubishi/Lancer-specific typo correction and update tests/docs. **Status: complete.**
3. Extract `HeuristicSearchRoutingClassifier` without behavior change. **Status: complete.**
4. Add `SEARCH_ROUTING_MODE=heuristic|off` wiring. **Status: complete.**
5. Add LLM search routing classifier behind `SEARCH_ROUTING_MODE=llm`. **Status: complete.**
6. Add safe command execution foundation with only read-only Git/status tools. **Status: complete for read-only Git tools.**
7. Add fixed `run_dotnet_tests` safe command tool or extract reusable `CommandSpec`/`SafeCommandExecutor` before medium-risk tools.
8. Add plugin manifest scanning without loading assemblies yet.
9. Add actual plugin assembly loading.
10. Add read-only GitHub tools.

This gives fast wins first, then unlocks bigger extensibility.

---

# Standard Verification Commands

Run after each patch:

```bash
cd /c/temp/TelegramMessagingTool
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
dotnet ef migrations has-pending-model-changes --project TelegramMessagingTool/TelegramMessagingTool.csproj --startup-project TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --no-build
dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
```

For release patches, continue current workflow:

1. publish to timestamped `release/TelegramMessagingTool-YYYYMMDD-HHMMSS`
2. update `.latest-release`
3. commit and push
4. stop old `TelegramMessagingTool.exe`
5. start latest release with token-bearing env
6. verify one latest process and Telegram API send

---

# Open Questions Before Implementation

1. Should plugin assemblies be allowed to execute immediately after loading, or should the first version only discover/list plugin tools?
2. Should GitHub tools use raw REST API or add a package such as Octokit?
3. Should `SEARCH_ROUTING_MODE=llm` become default immediately, or stay opt-in until tested live?
4. Which safe command tools should be enabled first: read-only Git tools, dotnet test runner, or project file readers?
5. Should high-risk model-generated actions always use `/pending`, or should admin chat be allowed to execute some medium-risk tools directly?
