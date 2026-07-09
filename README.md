# TelegramMessagingTool

TelegramMessagingTool is a C#/.NET console application that connects a Telegram bot to a local Ollama chat model and stores per-user conversation history in SQL Server LocalDB via Entity Framework Core.

## Features

- Telegram long-polling bot using `Telegram.Bot`
- Local Ollama chat completion endpoint support
- Per-user conversation history stored in SQL Server
- EF Core migrations
- Optional admin notification when a new user connects
- Optional chat allowlist via environment variable
- Long Telegram responses are split into safe chunks
- Safe agent tool system with bounded multi-step tool calling
- Built-in tools: `datetime`, `calculator`, `status`, optional `online_search` when `ENABLE_ONLINE_SEARCH=true`, optional read-only GitHub tools when `ENABLE_GITHUB_TOOLS=true`, and optional fixed safe command tools when `ENABLE_SAFE_COMMAND_TOOLS=true`
- `/tools` command to show available tools
- Admin-only `/riskconfig` command summarizes risky runtime feature flags without printing tokens, database connection strings, or provider credentials
- `/health` command reports compact runtime diagnostics including uptime, DB/migration status, model routes, search mode, plugin manifest counts, storage roots, and risk warning count without secrets
- Admin-only `/errors [count]` command shows a bounded, metadata-only in-memory history of recent runtime warnings/errors with secret/token redaction
- Lightweight Telegram reactions are sent best-effort for successful `/approve`, `/deny`, `/done`, `/remember`, and `/reset` commands while still keeping normal text replies
- Optional Telegram typing indicators for normal chat replies when `ENABLE_TELEGRAM_TYPING_INDICATOR=true`; commands and file/voice handling skip the indicator to avoid noisy UX
- Ollama streaming infrastructure is staged behind `ENABLE_STREAMING_RESPONSES=false`: the client can stream deltas, and `StreamingResponseService` falls back to non-streaming replies on streaming errors before Telegram edit-in-place UX is enabled
- Telegram edit-in-place infrastructure is staged through `TelegramStreamEditService`: it creates a draft-message workflow, throttles intermediate edits, trims edits to Telegram's message limit, and always performs finalization before runtime wiring is enabled
- Agent streaming safety boundary is staged in `AgentRunner.RunStreamingSafeAsync`: it streams only when no tools are registered, otherwise it falls back to the existing non-streaming tool-call loop so raw tool-call JSON is not exposed to Telegram users
- Adaptive reasoning guidance for complex normal chat prompts: planning, debugging, comparison, migration, and long multi-question requests get a private checklist plus final-answer discipline without exposing chain-of-thought.
- Runtime streaming wiring is available behind `ENABLE_STREAMING_RESPONSES=true`: normal Telegram messages use draft/edit streaming only when the agent safety boundary allows it; commands, files, voice, and tool-enabled agent flows remain on the established non-streaming path
- Agent-style startup console panel with commands, model, safety, and tool status
- Local console chat/command input using the same command router, memory, tools, and agent runner as Telegram, plus local-only `/dashboard` and `/logs [count]` runtime views with compact counters, recent redacted events, and secret-masked database details
- Runtime composition now builds runtime services through `Runtime/AppServicesBuilder.cs`, command registration through `Runtime/CommandRouterFactory.cs`, Telegram update handling through `Runtime/TelegramUpdateHandler.cs`, local console input through `Runtime/ConsoleInputHandler.cs`, and reminder polling through `Runtime/TaskReminderLoop.cs`, keeping `Program.cs` thin while preserving behavior
- Plugin/tool abstractions are shared from `TelegramMessagingTool.Abstractions` so future plugin assemblies can compile against stable `IAgentTool`, `ToolResult`, and `ToolRiskLevel` contracts with first-class risk/read-only/safety metadata
- Trusted local plugin loading is available behind `ENABLE_PLUGINS=true`; plugin DLLs must live under `PLUGIN_DIRECTORY`, pass manifest validation including compatible `apiVersion`, expose allowlisted `IAgentTool` names, avoid duplicates, and appear in `/tools` with `source`, `risk`, read-only, and safety metadata. State-changing plugin tools currently run directly when called, so keep them reviewed, sandboxed, and disabled unless trusted.
- Live console event lines for startup, commands, messages, denied users, shutdown, errors, and operational observability metadata for tool calls, pending-action creation/decisions, approval execution, repo write results, and GitHub API failures without logging raw message content by default
- Sandboxed document/file support for `.txt`, `.md`, `.json`, `.csv`, `.pdf`, `.docx`, `.xlsx`, `.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, `.mp3`, `.wav`, `.m4a`, `.ogg`, `.oga`, `.opus`, and `.flac`
- File commands: `/files`, `/images`, `/describeimage <id>`, `/voicefiles`, `/transcribe <id>`, `/transcriptinsights <id>`, `/transcripttasks <id>`, `/speaktext <text>`, `/sendaudio <id>`, `/exportchat [txt|docx|pdf] [last N]`, `/exportdata [json]`, `/readfile <id>`, `/createfile <filename> <content>`, admin-only local import via `/importfiles` and `/importfile <filename>`, and admin-approved `/deletefile <id>`
- `/exportdata [json]` creates a sandboxed JSON backup for the current chat/user covering chat messages, memories, file metadata, document chunk metadata, tasks, and pending-action history without exporting secrets or other users' data.
- Document Q&A indexing, question, summary, embedding, and vector maintenance commands: `/indexfile`, `/indexdocs`, `/docchunks`, `/askfile`, `/askdocs`, `/summarizefile`, `/summarizedocs`, `/embedfile`, `/embeddocs`, `/reembeddocs`, `/vectorstatus`, `/vectorsync`, `/vectorclear`, and `/vectorrepair`
- Vector-store abstraction foundation for scalable document retrieval: `IVectorStore`, `DocumentVector`, `VectorSearchResult`, and a tested `LocalJsonVectorStore` fallback. `/embedfile`, `/embeddocs`, and `/reembeddocs` can mirror embeddings into the configured vector store; `/askfile` and `/askdocs` search vector results first when `ENABLE_DOCUMENT_EMBEDDINGS=true` and fall back to SQL `DocumentChunk.EmbeddingJson`/lexical retrieval on provider failures.
- `/health` includes database/migration state, model routing, vector/Qdrant provider state, media provider gates, reasoning/runtime response flags, GitHub push readiness, storage paths, and risk-warning count without showing secrets.
- Admin-only `/selfupdate [reason]` creates a high-risk approval request to publish a timestamped release, update `.latest-release`, and restart the bot from the latest release after `/approve`.
- Read-only local device commands: `/systeminfo`, `/diskstatus`, and `/processes [count]`
- Approval foundation and executed risky tools for admin-reviewed local/process, file, repo, release/restart, and GitHub write actions
- Approval commands: `/pending`, `/approve <id>`, `/deny <id>`, `/action <id>`, and `/actions [count]` with structured previews/history showing exact risk, target file/repository, diff or git command summaries, GitHub issue/comment targets, and compact decision notes without dumping raw edit payloads
- Repo safety scanning before approved commits, pushes, and releases: blocks token-like diff additions, `.env`/secret config files, local DB/certificate/backup binaries, release outputs, and generated/runtime paths
- Task planner commands: `/plan <goal>`, `/tasks`, `/task <id>`, `/done <task-id> [step-number]`, and `/cancel <task-id>`
- `/harnesses` shows the `image_agent` and `voice_agent` safety roadmap, readiness status, provider gates, implemented command coverage, and next safe command candidates for image/voice workflows
- Plugin manifest inspection and trusted plugin loading via `/plugins`/`/tools`; manifests show paths and entry assembly presence, and enabled trusted DLLs can be loaded from `PLUGIN_DIRECTORY` when `ENABLE_PLUGINS=true`
- Plugin authoring starter docs/templates in `docs/plugin-authoring.md` and `plugins/SamplePlugin/plugin.json.example`

## Development note

Major agent-upgrade work on this project was implemented with help from **Hermes Ghost**, Mujahed Issa's local AI development assistant, including the command system, memory, tools, document handling, startup launcher, search improvements, and approval foundation.

## Requirements

- Windows with .NET SDK 10.0 or compatible runtime
- SQL Server LocalDB, or another SQL Server connection string
- A Telegram bot token from BotFather
- Ollama running locally or reachable over HTTP
- The configured Ollama model pulled locally, for example:

```bash
ollama pull llama3.2:3b
ollama pull nomic-embed-text
```

## Configuration

Configuration is read from environment variables.

| Variable | Required | Default | Description |
|---|---:|---|---|
| `TELEGRAM_BOT_TOKEN` | Yes | none | Telegram bot token from BotFather |
| `ADMIN_CHAT_ID` | No | `0` | Admin chat ID for new-user and blocked-user notifications |
| `ALLOWED_CHAT_IDS` | No | empty | Comma-separated Telegram chat IDs allowed to use the bot. Empty no longer means allow all unless `ALLOW_PUBLIC_ACCESS=true`. `ADMIN_CHAT_ID` is always allowed. |
| `ALLOW_PUBLIC_ACCESS` | No | `false` | If true and `ALLOWED_CHAT_IDS` is empty, any Telegram user who finds the bot can use it. Accepts `true`, `1`, or `yes`. Keep false for real use. |
| `OLLAMA_URL` | No | `http://localhost:11434/api/chat` | Ollama chat API endpoint |
| `OLLAMA_MODEL` | No | `llama3.2:3b` | Base Ollama model name and fallback for every route |
| `OLLAMA_MODEL_CHAT` | No | `OLLAMA_MODEL` | Model route for normal chat |
| `OLLAMA_MODEL_PLAN` | No | `OLLAMA_MODEL` | Model route for `/plan` and future planning features |
| `OLLAMA_MODEL_DOC_QA` | No | `OLLAMA_MODEL` | Model route for `/askfile` and `/askdocs` |
| `OLLAMA_MODEL_SUMMARY` | No | `OLLAMA_MODEL` | Model route for `/summarizefile` and `/summarizedocs` |
| `OLLAMA_MODEL_TOOL_FINAL` | No | `OLLAMA_MODEL` | Model route for tool final-answer synthesis |
| `OLLAMA_MODEL_IMAGE` | No | `OLLAMA_MODEL` | Model route for `/describeimage <id>` when `ENABLE_IMAGE_VISION=true`. Recommended local vision model after `ollama pull`: `llama3.2-vision:11b`. |
| `OLLAMA_MODEL_VOICE` | No | `OLLAMA_MODEL` | Model route for `/transcriptinsights <id>` transcript summarization/task extraction. Audio transcription uses the local command provider below, not Ollama directly. |
| `OLLAMA_EMBEDDING_URL` | No | derived from `OLLAMA_URL` as `/api/embed` | Ollama embedding API endpoint |
| `OLLAMA_EMBEDDING_MODEL` | No | `nomic-embed-text` | Local embedding model used by `/embedfile` and `/embeddocs` |
| `ENABLE_DOCUMENT_EMBEDDINGS` | No | `false` | If true, `/askfile` and `/askdocs` use stored embeddings for hybrid semantic retrieval when available |
| `VECTOR_STORE_PROVIDER` | No | `embedding_json` | Vector-store provider for document retrieval. `embedding_json` keeps the SQL `DocumentChunk.EmbeddingJson` path only. `local_json` mirrors embeddings to `VECTOR_STORE_PATH`. `qdrant` uses the Qdrant HTTP provider. Vector providers let `/askfile`/`/askdocs` try vector-store search first when `ENABLE_DOCUMENT_EMBEDDINGS=true`, with SQL/lexical fallback on errors. |
| `VECTOR_STORE_PATH` | No | `<current working directory>/VectorStore/vectors.json` | Local JSON vector-store file used only when `VECTOR_STORE_PROVIDER=local_json`. Prototype/local fallback only; use with trusted local storage. |
| `QDRANT_URL` | No | `http://localhost:6333` | Qdrant base URL used only when `VECTOR_STORE_PROVIDER=qdrant`. |
| `QDRANT_COLLECTION` | No | `telegram_documents` | Qdrant collection name used only when `VECTOR_STORE_PROVIDER=qdrant`. |
| `ENABLE_ONLINE_SEARCH` | No | `false` | If true, registers `online_search` and lets the agent use public web search for current facts. Keep false when you want offline/private behavior. |
| `SEARCH_ROUTING_MODE` | No | `heuristic` | Controls direct web-search routing before normal chat. `heuristic` uses the current keyword/current-facts classifier; `llm` asks the chat model for a strict JSON search/no-search decision; `off` disables direct search routing while keeping model-requested `online_search` available when registered. |
| `ENABLE_IMAGE_VISION` | No | `false` | If true, `/describeimage <id>` sends the selected sandboxed image to the configured local Ollama image route for a description. Keep false to return metadata only. |
| `IMAGE_DESCRIPTION_PROMPT` | No | safe concise default | Optional custom prompt used by `/describeimage <id>` when `ENABLE_IMAGE_VISION=true`. It is trimmed and capped at 1000 characters. Use this to focus descriptions on UI labels, visible text, diagrams, or general scene details. |
| `ENABLE_AUDIO_TRANSCRIPTION` | No | `false` | If true, `/transcribe <id>` and Telegram voice-message intake can execute the configured trusted local audio transcription command. Keep false until `AUDIO_TRANSCRIPTION_COMMAND` is configured and tested locally. |
| `AUDIO_TRANSCRIPTION_COMMAND` | No | empty | Local executable for audio transcription, for example a trusted Whisper/whisper.cpp wrapper. The app runs this directly with `UseShellExecute=false`; it is not a shell command string. |
| `AUDIO_TRANSCRIPTION_ARGUMENTS` | No | `{file}` | Argument template for the local transcription command. `{file}` is replaced with the selected sandboxed audio path. Quote it as `"{file}"` if the provider expects a path argument that may contain spaces. |
| `AUDIO_TRANSCRIPTION_TIMEOUT_SECONDS` | No | `120` | Local transcription timeout, clamped from `5` to `300` seconds. |
| `ENABLE_TEXT_TO_SPEECH` | No | `false` | If true, `/speaktext <text>` can execute the configured trusted local TTS command, and Telegram voice-message replies can be synthesized after the model answers. `/speaktext` still saves output only; automatic sending happens only for inbound Telegram voice messages. |
| `TEXT_TO_SPEECH_COMMAND` | No | empty | Local executable for text-to-speech, for example a trusted Piper/edge-tts wrapper. The app runs this directly with `UseShellExecute=false`; it is not a shell command string. |
| `TEXT_TO_SPEECH_ARGUMENTS` | No | `{text} {output}` | Argument template for the local TTS command. `{text}` is replaced with the requested text and `{output}` with a temporary output audio path. |
| `TEXT_TO_SPEECH_TIMEOUT_SECONDS` | No | `120` | Local TTS timeout, clamped from `5` to `300` seconds. |
| `TEXT_TO_SPEECH_OUTPUT_EXTENSION` | No | `.mp3` | Expected provider output extension: `.mp3`, `.wav`, `.m4a`, `.ogg`, `.oga`, `.opus`, or `.flac`. Use `.ogg`/`.oga`/`.opus` from an Opus-capable provider when you want Telegram voice-note bubbles; other audio formats are sent as normal audio files for voice-message replies. |
| `ENABLE_TELEGRAM_TYPING_INDICATOR` | No | `false` | If true, normal Telegram chat messages send best-effort `typing...` chat actions while the local agent is generating a reply. Slash commands, file commands, document handling, and voice handling are not wrapped. This is the safe first rollout step before streamed responses. |
| `ENABLE_STREAMING_RESPONSES` | No | `false` | If true, normal Telegram messages may use draft/edit streamed responses only when the agent safety boundary allows it. If tools are registered, commands/files/voice are used, or the safety boundary blocks streaming, the bot falls back to the established non-streaming path. Keep false unless you intentionally want experimental edit-in-place streaming UX. |
| `ENABLE_SAFE_COMMAND_TOOLS` | No | `false` | If true, registers fixed safe command tools: `git_status`, `git_diff`, `git_log_recent`, `run_dotnet_tests`, `publish_release`, and `restart_latest_bot`. No arbitrary shell access is exposed. `run_dotnet_tests` accepts only `{"target":"helper-tests"}` and runs the helper test project. `publish_release` and `restart_latest_bot` only create high-risk pending approval requests; they do not execute release/restart directly. |
| `SAFE_COMMAND_PROJECT_ROOT` | No | current working directory | Project root used by safe command tools and repo write approval tools. Commands run with fixed executable/argument lists under this directory. |
| `ENABLE_REPO_WRITE_TOOLS` | No | `false` | If true, registers approval-backed repository write tools such as `repo_replace_text`. These tools require admin use, create pending actions first, validate paths under `SAFE_COMMAND_PROJECT_ROOT`, and execute only after `/approve`. |
| `ENABLE_PLUGINS` | No | `false` | If true, enables plugin manifest discovery and trusted local plugin loading from `PLUGIN_DIRECTORY`. Use `/plugins` for manifest diagnostics and `/tools` to verify loaded plugin tools. |
| `PLUGIN_DIRECTORY` | No | `<current working directory>/plugins` | Directory scanned by `/plugins` for plugin folders containing `plugin.json`. Plugin assemblies are trusted OS-level code and should only come from reviewed/trusted sources. |
| `ENABLE_GITHUB_TOOLS` | No | `false` | If true, registers read-only GitHub tools such as `github_repo_info`. Keep false unless you want the model to query GitHub. |
| `ENABLE_GITHUB_WRITE_TOOLS` | No | `false` | If true, registers approval-backed GitHub write tools such as `github_create_issue` when a pending-action context is available. Requires `GITHUB_TOKEN`; keep false unless intentional. |
| `GITHUB_TOKEN` | No | empty | Optional token for GitHub API requests. Never log or paste it into chat. `/status` only reports configured/not configured. |
| `GITHUB_DEFAULT_OWNER` | No | empty | Default owner used by `github_repo_info` when the tool input is empty. |
| `GITHUB_DEFAULT_REPO` | No | empty | Default repo used by `github_repo_info` when the tool input is empty. |
| `GITHUB_ALLOWED_REPOS` | No | default repo if configured | Comma-separated `owner/repo` allowlist for GitHub tools. Repositories outside this list are rejected. |
| `TELEGRAM_DB_CONNECTION` | No | LocalDB connection | SQL Server connection string |
| `APPLY_MIGRATIONS` | No | `true` | Apply EF migrations on startup |
| `LOG_MESSAGE_CONTENT` | No | `false` | Log user messages and assistant responses. Keep disabled for privacy. |
| `CONVERSATION_MAX_HISTORY` | No | `8` | Number of recent persisted chat messages included in normal agent context. Values are clamped from `1` to `50`. |

Use `/riskconfig` from the admin chat to review high-risk local machine settings such as `ALLOW_PUBLIC_ACCESS=true`, `LOG_MESSAGE_CONTENT=true`, repo/GitHub write tools, trusted plugin loading, safe command tools, `SEARCH_ROUTING_MODE=llm`, and media provider gates with missing commands. The command reports only enabled/disabled/configured status and intentionally never prints token values, database connection strings, or provider secrets.

### Optional local Windows User environment profiles

For Mujahed's local development machine, the repository includes reversible PowerShell profile helpers. These scripts write **Windows User** environment variables only, never project defaults, and never write tokens, connection strings, provider command paths, or other secrets.

```powershell
# Enable non-secret local development feature flags for this Windows user.
.\scripts\Set-LocalDevEnvironment.ps1

# Optional, only if you intentionally want public/local test access or message-content logs:
.\scripts\Set-LocalDevEnvironment.ps1 -EnablePublicAccess -EnableContentLogging

# Revert risky feature flags to safer values.
.\scripts\Set-SafeEnvironment.ps1
```

Restart the bot process or terminal after running either script so the updated User environment is loaded. Keep secrets such as `TELEGRAM_BOT_TOKEN`, `GITHUB_TOKEN`, `TELEGRAM_DB_CONNECTION`, `AUDIO_TRANSCRIPTION_COMMAND`, and `TEXT_TO_SPEECH_COMMAND` configured separately.

Example Git Bash setup:

```bash
export TELEGRAM_BOT_TOKEN='123456:your-token'
export ADMIN_CHAT_ID='123456789'
export ALLOWED_CHAT_IDS='123456789,987654321'
export ALLOW_PUBLIC_ACCESS='false'
export OLLAMA_MODEL='llama3.2:3b'
# Optional route-specific overrides; blank/unset routes fall back to OLLAMA_MODEL.
export OLLAMA_MODEL_PLAN='llama3.2:3b'
export OLLAMA_MODEL_DOC_QA='llama3.2:3b'
export OLLAMA_MODEL_IMAGE='llama3.2-vision:11b'
export OLLAMA_MODEL_VOICE='llama3.2:3b'
export OLLAMA_EMBEDDING_MODEL='nomic-embed-text'
export ENABLE_DOCUMENT_EMBEDDINGS='false'
export ENABLE_ONLINE_SEARCH='false'
export SEARCH_ROUTING_MODE='heuristic'
export ENABLE_IMAGE_VISION='false'
export IMAGE_DESCRIPTION_PROMPT='Describe this image clearly and concisely. Mention visible text only if you can read it. Do not invent details you cannot see.'
export ENABLE_AUDIO_TRANSCRIPTION='false'
# Optional trusted local transcription provider, for example a Whisper wrapper:
# export AUDIO_TRANSCRIPTION_COMMAND='whisper-cli'
# export AUDIO_TRANSCRIPTION_ARGUMENTS='--model C:/models/ggml-base.en.bin --file "{file}"'
export AUDIO_TRANSCRIPTION_TIMEOUT_SECONDS='120'
export ENABLE_TEXT_TO_SPEECH='false'
# Optional trusted local TTS provider, for example a Piper/edge-tts wrapper:
# export TEXT_TO_SPEECH_COMMAND='tts-cli'
# export TEXT_TO_SPEECH_ARGUMENTS='--text "{text}" --out "{output}"'
export TEXT_TO_SPEECH_TIMEOUT_SECONDS='120'
export TEXT_TO_SPEECH_OUTPUT_EXTENSION='.mp3'
export ENABLE_TELEGRAM_TYPING_INDICATOR='false'
export ENABLE_STREAMING_RESPONSES='false'
export ENABLE_SAFE_COMMAND_TOOLS='false'
export SAFE_COMMAND_PROJECT_ROOT='/c/temp/TelegramMessagingTool'
export ENABLE_REPO_WRITE_TOOLS='false'
export ENABLE_PLUGINS='false'
export PLUGIN_DIRECTORY='/c/temp/TelegramMessagingTool/plugins'
export ENABLE_GITHUB_TOOLS='false'
export ENABLE_GITHUB_WRITE_TOOLS='false'
export GITHUB_DEFAULT_OWNER='mujahedgt'
export GITHUB_DEFAULT_REPO='TelegramMessagingTool'
export GITHUB_ALLOWED_REPOS='mujahedgt/TelegramMessagingTool,mujahedgt/IsolationForestServer'
export LOG_MESSAGE_CONTENT='false'
export CONVERSATION_MAX_HISTORY='8'
```

## Build

```bash
dotnet restore TelegramMessagingTool.slnx
dotnet build TelegramMessagingTool.slnx --configuration Release
```

## Test

The repository includes a small dependency-free console test project for helper logic, command behavior, scripted agent behavior evals, and safety checks.

```bash
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release
```

Expected output:

```text
All TelegramMessagingTool helper tests passed.
```

## Publish / Release

```bash
dotnet publish TelegramMessagingTool/TelegramMessagingTool.csproj \
  --configuration Release \
  --output release/TelegramMessagingTool
```

Run the released app:

```bash
./release/TelegramMessagingTool/TelegramMessagingTool.exe
```

## Agent tools

The bot can now use up to three safe tool steps per normal user request when the model replies with this strict JSON protocol:

```json
{"type":"tool_call","tool":"calculator","input":"25 * 19"}
```

Available tools:

| Tool | Approval | Purpose |
|---|---:|---|
| `datetime` | No | Current UTC and local server time |
| `calculator` | No | Safe arithmetic expressions only |
| `status` | No | Runtime configuration summary |
| `online_search` | No | Optional. Registered only when `ENABLE_ONLINE_SEARCH=true`. Public web search through DuckDuckGo Lite, Startpage, and Mojeek fallbacks; uses clean query variants and expands vehicle searches with price/spec terms |
| `github_repo_info` | No | Optional. Registered only when `ENABLE_GITHUB_TOOLS=true`. Read-only metadata for a repo in `GITHUB_ALLOWED_REPOS` |
| `github_list_issues` | No | Optional. Registered only when `ENABLE_GITHUB_TOOLS=true`. Lists issues for a repo in `GITHUB_ALLOWED_REPOS`; pull requests are excluded |
| `github_get_issue` | No | Optional. Registered only when `ENABLE_GITHUB_TOOLS=true`. Shows one issue's title, state, labels, assignees, timestamps, body excerpt, and URL for a repo in `GITHUB_ALLOWED_REPOS`; pull requests are rejected |
| `github_list_prs` | No | Optional. Registered only when `ENABLE_GITHUB_TOOLS=true`. Lists pull requests for a repo in `GITHUB_ALLOWED_REPOS` with state, author, branches, draft/ready status, timestamps, and URL |
| `github_get_pr_status` | No | Optional. Registered only when `ENABLE_GITHUB_TOOLS=true`. Shows one pull request's mergeability, draft/merged state, branch refs, change counts, comments/review comments, requested reviewers, timestamps, and URL |
| `github_create_issue` | Yes | Optional. Registered only when `ENABLE_GITHUB_WRITE_TOOLS=true` and an approval context is available. Creates a pending action for issue creation in a repo from `GITHUB_ALLOWED_REPOS`; GitHub is called only after `/approve` |
| `github_comment_issue` | Yes | Optional. Registered only when `ENABLE_GITHUB_WRITE_TOOLS=true` and an approval context is available. Creates a pending action to comment on an issue in a repo from `GITHUB_ALLOWED_REPOS`; GitHub is called only after `/approve` |
| `git_status` | No | Optional. Registered only when `ENABLE_SAFE_COMMAND_TOOLS=true`. Read-only `git status --short --branch` for `SAFE_COMMAND_PROJECT_ROOT` |
| `git_diff` | No | Optional. Registered only when `ENABLE_SAFE_COMMAND_TOOLS=true`. Read-only `git diff -- .` for `SAFE_COMMAND_PROJECT_ROOT` |
| `git_log_recent` | No | Optional. Registered only when `ENABLE_SAFE_COMMAND_TOOLS=true`. Read-only `git log --oneline -5` for `SAFE_COMMAND_PROJECT_ROOT` |
| `run_dotnet_tests` | No | Optional. Registered only when `ENABLE_SAFE_COMMAND_TOOLS=true`. Fixed helper test command only; input must be `{"target":"helper-tests"}` |
| `publish_release` | Yes | Optional. Registered only when `ENABLE_SAFE_COMMAND_TOOLS=true` and an approval context is available. After `/approve`, runs repo safety scanning, publishes a timestamped release, and updates `.latest-release`; does not restart |
| `restart_latest_bot` | Yes | Optional. Registered only when `ENABLE_SAFE_COMMAND_TOOLS=true` and an approval context is available. Schedules a safe restart from `.latest-release` after `/approve` using environment handoff |
| `repo_replace_text` | Yes | Optional. Registered only when `ENABLE_REPO_WRITE_TOOLS=true` and an approval context is available. Replaces one exact text block in an allowed project text file after `/approve` |
| `repo_apply_patch` | Yes | Optional. Registered only when `ENABLE_REPO_WRITE_TOOLS=true` and an approval context is available. Applies one validated unified diff after `/approve`; rejects binary/generated/runtime/out-of-root paths |
| `repo_commit_changes` | Yes | Optional. Registered only when `ENABLE_REPO_WRITE_TOOLS=true` and an approval context is available. Runs Git checks plus repo safety scanning, then commits current allowed project changes after `/approve`; does not push |
| `repo_push_changes` | Yes | Optional. Registered only when `ENABLE_REPO_WRITE_TOOLS=true` and an approval context is available. Runs repo safety scanning, refuses dirty working trees, and pushes the current branch to `origin` after `/approve`; no force push |
| `sample_echo` | No | Sample trusted plugin tool from `plugins/SamplePlugin`; echoes input when `ENABLE_PLUGINS=true` and the sample manifest/DLL are present |
| `dotnet_create_project` | No | Sample trusted plugin tool from `plugins/SamplePlugin`; creates a minimal .NET console project only under `GeneratedProjects/<name>`, supports `basic` and `nearest_friday` templates, and refuses overwrite/traversal paths |

Search behavior notes:

- When `ENABLE_ONLINE_SEARCH=true`, a heuristic search-routing classifier can directly use `online_search` for current facts, prices, market values, specs, products, cars, and news.
- Set `SEARCH_ROUTING_MODE=llm` to ask the chat model for a strict JSON search/no-search decision before normal answering. Invalid classifier JSON fails safe to no direct search.
- Set `SEARCH_ROUTING_MODE=off` to disable direct web-search routing; the model can still request `online_search` through the normal tool-call loop when the tool is registered.
- When `ENABLE_ONLINE_SEARCH=false`, the tool is not registered or advertised; the bot should say live web search is disabled instead of guessing current facts.
- The bot now hides raw `tool_call` JSON from the final answer after the tool runs.
- The search tool preserves the user's original query after whitespace cleanup. It no longer applies hardcoded domain-specific typo corrections; the model should correct obvious spelling only when the intended term is clear from context.
- Final search answers should summarize only what the returned search results support and include useful source links.

GitHub tool notes:

- `ENABLE_GITHUB_TOOLS=false` by default.
- `github_repo_info`, `github_list_issues`, `github_get_issue`, `github_list_prs`, and `github_get_pr_status` are read-only and reject repositories outside `GITHUB_ALLOWED_REPOS`.
- `github_list_issues` accepts optional JSON like `{ "owner": "mujahedgt", "repo": "TelegramMessagingTool", "state": "open", "limit": 10 }`; state must be `open`, `closed`, or `all`, and limit is clamped to `1..50`.
- `github_get_issue` accepts JSON like `{ "owner": "mujahedgt", "repo": "TelegramMessagingTool", "number": 123 }`; owner/repo can be omitted to use the configured default repo. It returns issue details only and rejects pull requests.
- `github_list_prs` accepts optional JSON like `{ "owner": "mujahedgt", "repo": "TelegramMessagingTool", "state": "open", "limit": 10 }`; state must be `open`, `closed`, or `all`, and limit is clamped to `1..50`.
- `github_get_pr_status` accepts JSON like `{ "owner": "mujahedgt", "repo": "TelegramMessagingTool", "number": 123 }`; owner/repo can be omitted to use the configured default repo. It reads PR metadata from GitHub's pull request detail endpoint.
- `GITHUB_TOKEN` is optional for read-only requests and is never rendered in tool output, `/status`, or docs examples.
- `ENABLE_GITHUB_WRITE_TOOLS=false` by default. `github_create_issue` and `github_comment_issue` are admin-only and approval-backed: they validate `GITHUB_ALLOWED_REPOS`, store pending actions without the token, and call GitHub only after `/approve <id>` using `GITHUB_TOKEN` from the runtime environment.
- `github_create_issue` accepts JSON like `{ "owner": "mujahedgt", "repo": "TelegramMessagingTool", "title": "Bug title", "body": "Details", "labels": ["bug"] }`; owner/repo can be omitted to use the configured default repo.
- `github_comment_issue` accepts JSON like `{ "owner": "mujahedgt", "repo": "TelegramMessagingTool", "number": 123, "body": "Comment text" }`; owner/repo can be omitted to use the configured default repo.

Multi-step tool loop notes:

- The agent can chain safe tools for up to three tool observations before it must produce a final answer.
- After each safe tool result, the model receives a structured observation and can either request one more safe tool or answer.
- If the model keeps requesting tools after the limit, the bot stops and asks the user to narrow or continue with a smaller task.

Safe command tool notes:

- `ENABLE_SAFE_COMMAND_TOOLS=false` by default.
- The first safe command tools are read-only Git inspection tools only: `git_status`, `git_diff`, and `git_log_recent`.
- `run_dotnet_tests` is a fixed helper-test runner only; it does not accept arbitrary projects, filters, commands, or arguments.
- `publish_release` is admin-only and approval-backed. It runs fixed `dotnet publish` for `TelegramMessagingTool/TelegramMessagingTool.csproj`, writes a timestamped folder under `release/`, updates `.latest-release` only after success, records the release path, and does not restart the bot.
- `restart_latest_bot` is admin-only and approval-backed. It validates `.latest-release`, creates a fixed restart script under `release/`, hands off runtime environment variables including `ENABLE_REPO_WRITE_TOOLS`, stops old `TelegramMessagingTool` processes from that script, and starts the latest release with the project root as working directory.
- `ENABLE_REPO_WRITE_TOOLS=false` by default.
- `repo_replace_text` is the first repo-write tool. It is admin-only, approval-backed, restricted to source/docs/config text files under `SAFE_COMMAND_PROJECT_ROOT`, rejects path traversal/generated/runtime folders, and replaces exactly one matching text block only after `/approve`.
- `repo_apply_patch` is admin-only and approval-backed. It accepts strict JSON `{ "patch": "unified diff", "reason": "why" }`, extracts affected paths from diff headers, rejects binary/generated/runtime/out-of-root paths, runs `git apply --check`, and only applies the patch after `/approve`.
- `repo_commit_changes` is admin-only and approval-backed. It runs `git diff --check`, refuses empty diffs, validates changed paths against the repo-write allowlist, commits with a strict JSON message/body, and never pushes.
- `repo_push_changes` is admin-only and approval-backed. It refuses dirty working trees, detects the current named branch, runs fixed `git push origin <current-branch>` with non-interactive Git environment variables, and never force-pushes.
- Review `git diff`/history before approving commit or push actions.

Risky tools such as arbitrary shell, broad file write/delete, database mutation, outbound messaging, direct commit/push, or unrestricted process control are intentionally not exposed as model tools. Use the approval flow before adding dangerous tools.

## Image and voice harnesses

The `/harnesses` command shows current feature-gate readiness and the safety roadmap for image and voice work:

| Harness | Current implemented gates | Later gated work |
|---|---|---|
| `image_agent` | `/images`, metadata-first `/describeimage <id>`, optional local image description behind `ENABLE_IMAGE_VISION=true` and `IMAGE_DESCRIPTION_PROMPT` | OCR extraction, image generation/prompting |
| `voice_agent` | `/voicefiles`, optional local `/transcribe <audio-id>`, transcript document storage, `/transcriptinsights <id>`, draft transcript-to-task planning via `/transcripttasks <id>`, optional sandboxed `/speaktext <text>` output storage, explicit `/sendaudio <id>` sandbox audio delivery, automatic voice/audio replies to inbound Telegram voice messages when providers are configured | richer voice workflows |

The image-agent foundation is implemented: `/images` lists sandboxed `.png/.jpg/.jpeg/.webp/.gif` files that were uploaded as Telegram documents, and `/describeimage <id>` returns safe metadata plus the configured image model route by default. When `ENABLE_IMAGE_VISION=true`, `/describeimage <id>` sends only that selected sandboxed image to the configured local Ollama image route for a concise description using `IMAGE_DESCRIPTION_PROMPT` (trimmed/capped to 1000 characters) so you can focus the model on UI labels, visible text, diagrams, or general scene details.

The voice-agent foundation includes `/voicefiles` for sandboxed `.mp3/.wav/.m4a/.ogg/.oga/.opus/.flac` files plus `/transcribe <audio-id>`. By default `/transcribe` remains metadata/readiness-only; when `ENABLE_AUDIO_TRANSCRIPTION=true` and `AUDIO_TRANSCRIPTION_COMMAND` is configured, it runs the trusted local provider command against only the selected sandboxed audio file, captures stdout as the transcript, saves successful transcripts back into the same user document sandbox as `*-transcript.txt` documents, and reports provider failures safely. `/speaktext <text>` is disabled by default; when `ENABLE_TEXT_TO_SPEECH=true` and `TEXT_TO_SPEECH_COMMAND` is configured, it runs a trusted local TTS provider and saves the generated audio into the sandbox without sending it automatically. Use `/sendaudio <audio-id>` to explicitly send a saved sandboxed audio file back to Telegram; `.ogg`/`.oga`/`.opus` files are sent as Telegram voice notes, and other audio formats are sent as normal audio files. Telegram voice messages are also supported: when transcription is configured, the bot saves the inbound voice note, transcribes it, stores the transcript in chat history, asks the local model, and then replies. If `ENABLE_TEXT_TO_SPEECH=true` and `TEXT_TO_SPEECH_COMMAND` is configured, the reply is synthesized and sent back as a Telegram voice note when the provider outputs `.ogg`/`.oga`/`.opus`; otherwise it is sent as a normal audio file. `/transcriptinsights <transcript-file-id>` analyzes only saved transcript text through `OLLAMA_MODEL_VOICE` and returns a concise voice summary, decisions/facts, action items, and open questions. See `docs/voice-provider-configuration.md` for Windows User environment-variable setup and provider wrapper examples.

## Command parsing

Commands are matched exactly. For example, `/statusx` is not treated as `/status`. Telegram group syntax such as `/status@your_bot_username` is accepted and parsed as `/status` with the same arguments.

## Read-only local device commands

These commands inspect the local machine without changing anything:

| Command | Purpose |
|---|---|
| `/systeminfo` | Show operating system, architecture, machine name, CPU count, .NET runtime, uptime, and process memory |
| `/diskstatus` | Show ready local drives, total space, free space, and used percentage |
| `/processes [count]` | Show running process count and top processes by memory usage; count is clamped to a safe range |
| `/killprocess <pid>` | Admin-only: create a high-risk pending approval request to terminate a process; execution happens only after `/approve <id>` |

The inspection commands are intentionally **read-only**. They do not stop processes, delete files, edit files, or run arbitrary shell commands.

`/killprocess <pid>` is the first risky local-control command. It creates a pending action with risk level `high`; the process is terminated only if you explicitly approve that exact pending action with `/approve <id>`. The executor refuses invalid PIDs, refuses to terminate the bot's own process, blocks known protected Windows process names, and records the execution result in the pending action decision note.

Risky approval commands are admin-only. Set `ADMIN_CHAT_ID` to your Telegram chat ID before using `/killprocess`, `/pending`, `/action`, `/approve`, or `/deny`. If `ADMIN_CHAT_ID` is not configured, these commands fail closed.

## Approval flow

The bot includes a database-backed approval system for risky actions. It stores, tracks, and executes only explicitly approved admin actions for supported local/process, file, repository, release/restart, and GitHub write workflows.

Commands:

| Command | Purpose |
|---|---|
| `/pending` | Admin-only: list pending actions waiting for your approval |
| `/action <id>` | Admin-only: show audit details for a pending, approved, denied, expired, or executed action |
| `/actions [count]` | Admin-only: list recent action audit records with compact status, risk, timestamps, decision notes, and `/action <id>` drill-down links |
| `/approve <id>` | Admin-only: approve a pending action |
| `/deny <id>` | Admin-only: deny a pending action |

Pending actions expire automatically if they are not approved before their expiry time. Use `/action <id>` before approving risky operations to review the action type, risk level, status, payload summary, timestamps, and decision/execution note.

## Task planner

The bot can create and track simple step-by-step plans per Telegram user.

Commands:

| Command | Purpose |
|---|---|
| `/plan <goal>` | Create a new task plan with practical steps |
| `/tasks` | List active task plans |
| `/tasks all` | List active, completed, and cancelled plans |
| `/task <id>` | Show one task plan with its steps |
| `/done <task-id> [step-number]` | Mark a step done, or mark the full task done if no step is given |
| `/cancel <task-id>` | Cancel an active task plan |

The first version uses deterministic plan templates for software/project, study/learning, and general goals. Later versions can connect this to the LLM for richer planning.

## File and document support

The bot can safely store, create, and read supported documents inside a per-chat sandbox under the app's `UserFiles/` directory.

Supported extensions:

```text
.txt, .md, .json, .csv, .pdf, .docx, .xlsx
```

Commands:

| Command | Purpose |
|---|---|
| `/files` | List your saved/uploaded files |
| `/exportchat [txt|docx|pdf] [last N]` | Export your recent chat history as a sandboxed TXT, DOCX, or PDF file and attach it back to Telegram. Defaults to TXT and clamps the count to a safe maximum. |
| `/readfile <id>` | Extract/read text from a saved file by ID |
| `/createfile <filename> <content>` | Create a sandboxed txt/md/json/csv/pdf/docx/xlsx file |
| `/importfiles` | Admin-only: list supported files placed in the local `ImportInbox/` folder |
| `/importfile <filename>` | Admin-only: copy one supported file from `ImportInbox/` into your document sandbox, bypassing Telegram upload/download limits |
| `/deletefile <id>` | Admin-only: create a high-risk approval request to delete a sandboxed saved file; deletion happens only after `/approve <id>` |
| `/indexfile <id>` | Extract and chunk one file for document Q&A |
| `/indexdocs` | Index all your saved files for document Q&A |
| `/docchunks <id>` | Show indexed chunk status for one file |
| `/askfile <id> <question>` | Ask a question about one saved/indexed file |
| `/askdocs <question>` | Ask a question across all indexed files |
| `/summarizefile <id>` | Summarize one saved/indexed file |
| `/summarizedocs` | Summarize all indexed files |
| `/embedfile <id>` | Generate local embeddings for one saved/indexed file |
| `/embeddocs` | Generate local embeddings for all indexed files |

Q&A behavior:

- `/askfile` auto-indexes the target file once if it has no chunks yet.
- `/askdocs` searches across already indexed chunks; run `/indexdocs` first if needed.
- Answers are produced from retrieved document excerpts and should cite file ID, filename, and chunk number.
- `/summarizefile` auto-indexes the target file once if it has no chunks yet.
- `/summarizedocs` summarizes already indexed chunks; run `/indexdocs` first if needed.
- Retrieval uses improved local lexical scoring with exact-phrase and multi-term boosts. Embeddings such as `bge-m3` can be added later for stronger semantic search.
- `/embedfile` and `/embeddocs` generate local Ollama embeddings for indexed chunks using `OLLAMA_EMBEDDING_MODEL`.
- If `ENABLE_DOCUMENT_EMBEDDINGS=true`, `/askfile` and `/askdocs` use hybrid semantic + lexical ranking when stored embeddings exist, with lexical fallback if embeddings are unavailable.

You can also upload a supported Telegram document directly. The bot stores it and replies with a file ID that can be used with `/readfile`, `/indexfile`, and `/askfile`.

Document extraction notes:

- `.txt`, `.md`, `.json`, `.csv` are read as UTF-8 text.
- `.pdf` text extraction works for normal text PDFs; scanned/image-only PDFs need OCR and are not supported yet.
- `.docx` extraction reads WordprocessingML text. Legacy binary `.doc` is not supported.
- `.xlsx` extraction reads visible worksheet cell values. Legacy binary `.xls` is not supported.

Safety model:

- files are stored under `UserFiles/<chatId>/`
- filenames are sanitized
- path traversal is blocked
- executable/binary file types are rejected
- only supported sandboxed files are read directly
- executable/binary file types outside the allowlist are rejected
- `/importfile <filename>` only accepts a plain filename from local `ImportInbox/` and copies it into the user sandbox; it never reads arbitrary paths
- `/deletefile <id>` is approval-backed and deletes only the stored sandbox file plus its database metadata/chunks after admin approval

## Console UI

On startup the console shows a readable agent dashboard:

- runtime status: bot username, model, Ollama endpoint, database summary, migrations, access mode, logging
- command list in compact columns
- registered agent tools
- quick-start examples that work from either Telegram or the local console
- safety warnings when access is locked, public access is explicitly enabled, or message-content logging is enabled
- live event stream for startup, commands, normal messages, denied users, shutdown, and errors

You can now use the agent directly from the console while the Telegram bot is running:

```text
> /help
> /tools
> Calculate 25 * 19 and then check the time
> /plan prepare document Q&A support
> /exit
```

Console input uses the same command router, local Ollama model, safe tool loop, memory tables, task planner, and sandboxed file commands as Telegram. The console identity is stored as a local user named `local_console` with chat ID `0`.

Sensitive connection-string fields such as passwords and user IDs are not printed in the startup panel.

## Runtime notes

- Type normal messages or slash commands directly in the console to use the same local agent without Telegram.
- Use `/dashboard` in the local console to refresh compact runtime counters: active tasks, pending approvals, indexed docs, saved files/images, recent warning/error count, uptime, access mode, and secret-masked database summary.
- Use `/logs [count]` in the local console to show recent in-memory runtime events, newest first. Count defaults to 20 and is clamped to 1-100. Tokens/secrets are redacted before rendering.
- Use `/exit` in the console to stop the bot gracefully.
- `Ctrl+C` also requests graceful shutdown.
- By default, the bot fails closed for Telegram access. Configure `ADMIN_CHAT_ID` or `ALLOWED_CHAT_IDS`. Use `ALLOW_PUBLIC_ACCESS=true` only for intentional local/public testing.
- By default, logs do **not** contain full message or response text. Set `LOG_MESSAGE_CONTENT=true` only for debugging and avoid sharing log files.
- Short Telegram network resets such as `SocketException (10054)` are treated as transient receiver errors. The console logs a compact `NET` warning and long polling continues automatically.
- The default database is:

```text
Server=(localdb)\MSSQLLocalDB;Database=TelegramMessagingTool;Trusted_Connection=True;TrustServerCertificate=True
```

## Project layout

```text
TelegramMessagingTool/
├─ TelegramMessagingTool.slnx
├─ README.md
├─ .gitignore
├─ TelegramMessagingTool/
│  ├─ Program.cs
│  ├─ BotRuntime.cs
│  ├─ Commands/
│  ├─ Agent/
│  ├─ Tools/
│  ├─ ConsoleUi/
│  ├─ Services/
│  ├─ Data/DbContext.cs
│  ├─ models/
│  └─ Migrations/
└─ TelegramMessagingTool.Tests/
   └─ Program.cs
```

## Security recommendations

1. Set `ADMIN_CHAT_ID` and/or `ALLOWED_CHAT_IDS` in production. Keep `ALLOW_PUBLIC_ACCESS=false` unless public access is intentional.
2. Keep `LOG_MESSAGE_CONTENT=false` unless actively debugging.
3. Store secrets in environment variables or a secret manager, not in source files.
4. Use a production database connection string outside local development.
5. Review published logs before sharing archives.
6. Keep `UserFiles/` private because it contains user-uploaded and bot-created documents.
