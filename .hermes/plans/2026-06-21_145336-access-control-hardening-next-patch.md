# Access Control Hardening Next Patch Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Make TelegramMessagingTool fail closed by default for Telegram access unless an explicit allowlist, admin chat ID, or public-access override is configured.

**Architecture:** Keep the current environment-variable configuration model, but add one explicit boolean setting: `ALLOW_PUBLIC_ACCESS`. Change `BotAccessPolicy.IsAllowed` so an empty `ALLOWED_CHAT_IDS` no longer means public access unless `ALLOW_PUBLIC_ACCESS=true`; the configured `ADMIN_CHAT_ID` should always be allowed. Update console warnings, README, tests, and release verification.

**Tech Stack:** C#/.NET 10 console app, Telegram.Bot, EF Core SQL Server LocalDB, existing dependency-free console test project.

---

## Current Review Snapshot

### Verification results from current HEAD

Current HEAD at review time:

```text
4e33cea
```

Current latest release pointer:

```text
release/TelegramMessagingTool-20260621-144555
```

Verification passed before writing this plan:

```text
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
# All TelegramMessagingTool helper tests passed.

dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
# Build succeeded. 0 Warning(s), 0 Error(s)

dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
# no vulnerable packages
```

### Main findings

| Priority | Finding | Evidence | Recommended action |
|---|---|---|---|
| P0 | Empty `ALLOWED_CHAT_IDS` currently allows anyone to use the bot | `BotRuntime.cs`, `BotAccessPolicy.IsAllowed` returns true when allowlist is empty | Fail closed unless `ALLOW_PUBLIC_ACCESS=true`; always allow `ADMIN_CHAT_ID` |
| P1 | Most commands still use loose `StartsWith("/command")` matching | 30+ matches in `TelegramMessagingTool/Commands/*.cs` | Plan a follow-up `CommandParser` patch after access hardening |
| P1 | `Program.cs` is large and mixes wiring, Telegram update handling, document handling, console loop, and reply sending | `Program.cs` is ~661 lines | Later extract `TelegramReplyService`, `UserService`, and `TelegramUpdateHandler` |
| P1 | Online search is always enabled and sends queries to external providers | `AgentRunner.TryBuildDirectSearchQuery`, `OnlineSearchTool` | Later add `ENABLE_ONLINE_SEARCH=false` default or explicit privacy gate |
| P2 | Tests are now large in one console file | `TelegramMessagingTool.Tests/Program.cs` is ~731 lines | Later migrate to xUnit/MSTest or split helper test classes |

## Why this is the next patch

The bot now has local file import, document Q&A, risky admin actions, and Telegram exposure. Even though risky commands are admin-only, normal chat, document upload, file creation, memory, and model/tool use are still reachable if the bot username/token is discovered and `ALLOWED_CHAT_IDS` is empty.

The next patch should protect the entire bot before adding more capabilities.

---

## Acceptance Criteria

- [ ] `ALLOW_PUBLIC_ACCESS` is added to `BotSettings` and loaded from environment.
- [ ] Default behavior is fail-closed for Telegram chats when neither `ALLOWED_CHAT_IDS` nor `ADMIN_CHAT_ID` nor `ALLOW_PUBLIC_ACCESS=true` is configured.
- [ ] `ADMIN_CHAT_ID` is allowed even if not listed in `ALLOWED_CHAT_IDS`.
- [ ] Existing configured allowlist still works.
- [ ] `ALLOW_PUBLIC_ACCESS=true` preserves current open-development behavior intentionally.
- [ ] Console startup panel clearly shows whether access mode is `allowlist`, `admin-only`, or `public override`.
- [ ] README documents the new variable and safe recommended setup.
- [ ] Tests cover all access-policy branches.
- [ ] Full verification passes: tests, build, EF model check, vulnerability scan, secret scan.
- [ ] Publish timestamped release, update `.latest-release`, commit, push, restart bot, verify long polling.

---

## Task 1: Extend `BotSettings` with explicit public-access setting

**Objective:** Add a first-class config flag for intentional public access.

**Files:**
- Modify: `TelegramMessagingTool/BotRuntime.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Step 1: Write failing tests**

Add access-policy assertions near the existing `BotAccessPolicy` tests:

```csharp
var emptyAllowlist = new HashSet<long>();
var configuredAllowlist = BotAccessPolicy.ParseAllowedChatIds("123,456");

AssertFalse(
    BotAccessPolicy.IsAllowed(999, emptyAllowlist, adminChatId: 0, allowPublicAccess: false),
    "empty allowlist fails closed when public access is not explicitly enabled");

AssertTrue(
    BotAccessPolicy.IsAllowed(999, emptyAllowlist, adminChatId: 0, allowPublicAccess: true),
    "public override allows unknown chat when explicitly enabled");

AssertTrue(
    BotAccessPolicy.IsAllowed(777, emptyAllowlist, adminChatId: 777, allowPublicAccess: false),
    "admin chat is allowed even without allowlist");

AssertTrue(
    BotAccessPolicy.IsAllowed(123, configuredAllowlist, adminChatId: 777, allowPublicAccess: false),
    "configured allowlist allows listed chat");

AssertFalse(
    BotAccessPolicy.IsAllowed(999, configuredAllowlist, adminChatId: 777, allowPublicAccess: false),
    "configured allowlist blocks unknown non-admin chat");
```

**Step 2: Verify RED**

Run:

```bash
cd /c/temp/TelegramMessagingTool
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
```

Expected: compile failure because the new overload/properties do not exist yet.

**Step 3: Update settings record**

In `TelegramMessagingTool/BotRuntime.cs`, change `BotSettings` to include:

```csharp
bool AllowPublicAccess,
```

Place it near `AllowedChatIds`.

**Step 4: Load environment variable**

In `BotConfiguration.LoadFromEnvironment()`, load:

```csharp
bool allowPublicAccess = IsEnabled(Environment.GetEnvironmentVariable("ALLOW_PUBLIC_ACCESS"), defaultValue: false);
```

Pass it to the `BotSettings` constructor.

**Step 5: Update every `new BotSettings(...)` call**

Expected locations:

- `TelegramMessagingTool/Program.cs` indirectly uses `LoadFromEnvironment`, no direct update needed there.
- `TelegramMessagingTool.Tests/Program.cs` has manual `new BotSettings(...)` objects.

Add:

```csharp
AllowPublicAccess: false,
```

for tests unless a test explicitly needs public mode.

**Step 6: Run test again**

Expected: still failing until access policy is updated.

---

## Task 2: Change `BotAccessPolicy.IsAllowed` to fail closed

**Objective:** Enforce secure access decisions in one place.

**Files:**
- Modify: `TelegramMessagingTool/BotRuntime.cs`
- Modify: `TelegramMessagingTool/Program.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Step 1: Add/replace access-policy method**

Replace current method:

```csharp
public static bool IsAllowed(long chatId, IReadOnlySet<long> allowedChatIds)
{
    return allowedChatIds.Count == 0 || allowedChatIds.Contains(chatId);
}
```

with:

```csharp
public static bool IsAllowed(
    long chatId,
    IReadOnlySet<long> allowedChatIds,
    long adminChatId,
    bool allowPublicAccess)
{
    if (IsAdmin(chatId, adminChatId))
    {
        return true;
    }

    if (allowedChatIds.Contains(chatId))
    {
        return true;
    }

    return allowPublicAccess && allowedChatIds.Count == 0;
}
```

**Step 2: Update `Program.cs` call site**

Find:

```csharp
if (!BotAccessPolicy.IsAllowed(message.Chat.Id, settings.AllowedChatIds))
```

Replace with:

```csharp
if (!BotAccessPolicy.IsAllowed(
        message.Chat.Id,
        settings.AllowedChatIds,
        settings.AdminChatId,
        settings.AllowPublicAccess))
```

**Step 3: Update tests that use old signature**

Replace old assertions like:

```csharp
BotAccessPolicy.IsAllowed(123, allowlist)
```

with the new signature.

Use `allowPublicAccess: true` only for the old “empty allowlist permits all for local development” compatibility test, then rename that test to make explicit it requires public override.

**Step 4: Run tests**

```bash
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
```

Expected: pass after all call sites are updated.

---

## Task 3: Improve denied-access user message and admin alert

**Objective:** Make the new fail-closed behavior understandable when a user is blocked.

**Files:**
- Modify: `TelegramMessagingTool/Program.cs`
- Optional Modify: `TelegramMessagingTool/BotRuntime.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs` if helper method is extracted

**Step 1: Extract message helper**

In `BotAccessPolicy`, add:

```csharp
public static string AccessDeniedMessage(bool allowPublicAccess, IReadOnlySet<long> allowedChatIds, long adminChatId)
{
    if (!allowPublicAccess && allowedChatIds.Count == 0 && adminChatId <= 0)
    {
        return "Access denied. The bot is locked because ALLOWED_CHAT_IDS is empty, ADMIN_CHAT_ID is not configured, and ALLOW_PUBLIC_ACCESS is not enabled.";
    }

    return "Access denied. Ask the bot administrator to add your chat ID.";
}
```

**Step 2: Use it in `Program.cs`**

Replace the hardcoded denial text with:

```csharp
text: BotAccessPolicy.AccessDeniedMessage(
    settings.AllowPublicAccess,
    settings.AllowedChatIds,
    settings.AdminChatId),
```

**Step 3: Test helper behavior**

Add tests:

```csharp
AssertTrue(
    BotAccessPolicy.AccessDeniedMessage(false, new HashSet<long>(), 0).Contains("ALLOW_PUBLIC_ACCESS"),
    "AccessDeniedMessage explains fully locked configuration");

AssertTrue(
    BotAccessPolicy.AccessDeniedMessage(false, new HashSet<long> { 123 }, 0).Contains("administrator"),
    "AccessDeniedMessage gives normal allowlist denial text");
```

**Step 4: Run tests**

Expected: pass.

---

## Task 4: Update console startup panel and warnings

**Objective:** Make the runtime access mode visible at startup.

**Files:**
- Modify: `TelegramMessagingTool/ConsoleUi/AgentConsoleRenderer.cs`
- Modify: `TelegramMessagingTool/Program.cs`
- Modify: any snapshot/record file if `AgentConsoleSnapshot` is defined separately
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Step 1: Inspect current snapshot type**

Read:

```text
TelegramMessagingTool/ConsoleUi/AgentConsoleRenderer.cs
```

Find `AgentConsoleSnapshot` fields and add a field such as:

```csharp
bool AllowPublicAccess
```

or a precomputed:

```csharp
string AccessMode
```

Prefer `AccessMode` to keep renderer simple.

**Step 2: Compute access mode in `Program.cs`**

At startup panel construction, pass one of:

```text
allowlist
admin-only
public override
locked
```

Suggested helper:

```csharp
public static string DescribeAccessMode(IReadOnlySet<long> allowedChatIds, long adminChatId, bool allowPublicAccess)
{
    if (allowPublicAccess && allowedChatIds.Count == 0) return "public override";
    if (allowedChatIds.Count > 0) return "allowlist";
    if (adminChatId > 0) return "admin-only";
    return "locked";
}
```

Put this helper in `BotAccessPolicy` and test it.

**Step 3: Update renderer warnings**

Current warning likely says:

```text
ALLOWED_CHAT_IDS is not set. Anyone who finds the bot can use it.
```

Change behavior:

- `public override`: warn strongly that anyone can use the bot.
- `locked`: warn that nobody can use Telegram until `ADMIN_CHAT_ID`, `ALLOWED_CHAT_IDS`, or `ALLOW_PUBLIC_ACCESS=true` is configured.
- `admin-only`: show safe info, not warning.
- `allowlist`: show safe info, not warning.

**Step 4: Update tests**

Existing console renderer tests assert the old warning. Update them to assert:

- Public override mode shows “Anyone who finds the bot can use it.”
- Locked mode shows “bot is locked” or equivalent.
- Allowlist/admin-only does not show the public warning.

**Step 5: Run tests**

Expected: pass.

---

## Task 5: Update README and environment examples

**Objective:** Document the safer access model so future runs are predictable.

**Files:**
- Modify: `README.md`

**Step 1: Add config variable**

In the configuration table add:

```markdown
| `ALLOW_PUBLIC_ACCESS` | No | `false` | If true and `ALLOWED_CHAT_IDS` is empty, any Telegram user who finds the bot can use it. Keep false for real use. |
```

**Step 2: Update `ALLOWED_CHAT_IDS` description**

Change:

```text
Empty means allow all.
```

To:

```text
Empty no longer means allow all unless ALLOW_PUBLIC_ACCESS=true. ADMIN_CHAT_ID is always allowed.
```

**Step 3: Update example env block**

Add:

```bash
export ALLOW_PUBLIC_ACCESS='false'
```

**Step 4: Update Runtime notes**

Replace the current warning:

```text
If ALLOWED_CHAT_IDS is empty, any Telegram user who finds the bot can use it.
```

with:

```text
By default the bot fails closed. Configure ADMIN_CHAT_ID or ALLOWED_CHAT_IDS. Use ALLOW_PUBLIC_ACCESS=true only for intentional local/public testing.
```

---

## Task 6: Full verification and release

**Objective:** Prove the access hardening patch works and deploy it safely.

**Files:**
- Modify: `.latest-release` after publish
- No DB migration expected

**Step 1: Run tests**

```bash
cd /c/temp/TelegramMessagingTool
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
```

Expected:

```text
All TelegramMessagingTool helper tests passed.
```

**Step 2: Build**

```bash
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
```

Expected:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

**Step 3: EF model check**

```bash
dotnet ef migrations has-pending-model-changes --project TelegramMessagingTool/TelegramMessagingTool.csproj --startup-project TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --no-build
```

Expected:

```text
No changes have been made to the model since the last migration.
```

**Step 4: Vulnerability scan**

```bash
dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
```

Expected: no vulnerable packages.

**Step 5: Secret scan**

```bash
git grep -nE '[0-9]{8,10}:[A-Za-z0-9_-]{30,}' -- ':!release' ':!bin' ':!obj' ':!UserFiles' ':!ImportInbox' || true
```

Expected: no real token output.

**Step 6: Publish release**

```bash
release_dir="release/TelegramMessagingTool-$(date +%Y%m%d-%H%M%S)"
dotnet publish TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --output "$release_dir" --nologo
printf '%s' "$release_dir" > .latest-release
test -f "$release_dir/TelegramMessagingTool.exe"
```

**Step 7: Commit and push**

```bash
git add .latest-release README.md TelegramMessagingTool/BotRuntime.cs TelegramMessagingTool/Program.cs TelegramMessagingTool/ConsoleUi/AgentConsoleRenderer.cs TelegramMessagingTool.Tests/Program.cs
git commit -m "Harden Telegram access defaults"
GCM_INTERACTIVE=Never GIT_TERMINAL_PROMPT=0 git -c credential.helper= -c credential.helper=manager push origin master
```

**Step 8: Restart latest release**

Stop old `TelegramMessagingTool.exe`, start `.latest-release` with user environment variables, and wait for:

```text
long polling is running
```

**Step 9: Runtime smoke tests**

From admin Telegram account:

```text
/status
/help
```

Expected: replies normally.

If a non-admin chat is available, verify it receives access denied unless explicitly allowlisted or `ALLOW_PUBLIC_ACCESS=true`.

---

## Risks and Tradeoffs

| Risk | Mitigation |
|---|---|
| User locks themselves out if `ADMIN_CHAT_ID` is missing and `ALLOWED_CHAT_IDS` is empty | Keep admin fallback, update README, and startup panel warns `locked` clearly |
| Existing local/dev setups relied on empty allowlist meaning public | Add explicit `ALLOW_PUBLIC_ACCESS=true` override |
| Tests and constructors break due to `BotSettings` signature change | Update all `new BotSettings(...)` call sites and compile verifies completeness |
| Startup shortcut may not have env vars | It already pulls user env vars before launch; verify runtime after release |

---

## Follow-up Plan After This Patch

After access hardening lands, the next best patch should be:

```text
Exact command parsing with /command@botname support
```

Reason: `StartsWith("/command")` still exists across most command handlers, so typo/prefix collisions remain possible. The fix should introduce a shared `CommandParser` and migrate commands incrementally with tests.
