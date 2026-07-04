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
- Agent-style startup console panel with commands, model, safety, and tool status
- Local console chat/command input using the same command router, memory, tools, and agent runner as Telegram
- Live console event lines for startup, commands, messages, denied users, shutdown, and errors
- Sandboxed document/file support for `.txt`, `.md`, `.json`, `.csv`, `.pdf`, `.docx`, `.xlsx`, `.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, `.mp3`, `.wav`, `.m4a`, `.ogg`, `.oga`, `.opus`, and `.flac`
- File commands: `/files`, `/images`, `/describeimage <id>`, `/voicefiles`, `/transcribe <id>`, `/readfile <id>`, `/createfile <filename> <content>`, admin-only local import via `/importfiles` and `/importfile <filename>`, and admin-approved `/deletefile <id>`
- Document Q&A indexing, question, summary, and embedding commands: `/indexfile`, `/indexdocs`, `/docchunks`, `/askfile`, `/askdocs`, `/summarizefile`, `/summarizedocs`, `/embedfile`, and `/embeddocs`
- Read-only local device commands: `/systeminfo`, `/diskstatus`, and `/processes [count]`
- Safe approval foundation for future risky tools
- Approval commands: `/pending`, `/approve <id>`, and `/deny <id>`
- Task planner commands: `/plan <goal>`, `/tasks`, `/task <id>`, `/done <task-id> [step-number]`, and `/cancel <task-id>`
- P2 planning harness command: `/harnesses` shows the planned `image_agent` and `voice_agent` tool/safety roadmap before implementation
- Read-only plugin manifest inspection via `/plugins`; this scans `plugin.json` files only, shows manifest paths and entry assembly presence, and does not load plugin assemblies
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
| `OLLAMA_MODEL_IMAGE` | No | `OLLAMA_MODEL` | Reserved model route for future image-agent features. Recommended local vision model after `ollama pull`: `llama3.2-vision:11b`. |
| `OLLAMA_MODEL_VOICE` | No | `OLLAMA_MODEL` | Reserved model route for future voice-agent transcript summarization/task extraction. Audio transcription itself still needs a dedicated transcription provider later. |
| `OLLAMA_EMBEDDING_URL` | No | derived from `OLLAMA_URL` as `/api/embed` | Ollama embedding API endpoint |
| `OLLAMA_EMBEDDING_MODEL` | No | `nomic-embed-text` | Local embedding model used by `/embedfile` and `/embeddocs` |
| `ENABLE_DOCUMENT_EMBEDDINGS` | No | `false` | If true, `/askfile` and `/askdocs` use stored embeddings for hybrid semantic retrieval when available |
| `ENABLE_ONLINE_SEARCH` | No | `false` | If true, registers `online_search` and lets the agent use public web search for current facts. Keep false when you want offline/private behavior. |
| `SEARCH_ROUTING_MODE` | No | `heuristic` | Controls direct web-search routing before normal chat. `heuristic` uses the current keyword/current-facts classifier; `llm` asks the chat model for a strict JSON search/no-search decision; `off` disables direct search routing while keeping model-requested `online_search` available when registered. |
| `ENABLE_IMAGE_VISION` | No | `false` | If true, `/describeimage <id>` sends the selected sandboxed image to the configured local Ollama image route for a description. Keep false to return metadata only. |
| `ENABLE_AUDIO_TRANSCRIPTION` | No | `false` | Reserved gate for `/transcribe <id>`. Keep false until a trusted local Whisper/audio transcription provider is configured; the current command returns metadata/provider-readiness only. |
| `ENABLE_SAFE_COMMAND_TOOLS` | No | `false` | If true, registers fixed safe command tools: `git_status`, `git_diff`, `git_log_recent`, `run_dotnet_tests`, `publish_release`, and `restart_latest_bot`. No arbitrary shell access is exposed. `run_dotnet_tests` accepts only `{"target":"helper-tests"}` and runs the helper test project. `publish_release` and `restart_latest_bot` only create high-risk pending approval requests; they do not execute release/restart directly. |
| `SAFE_COMMAND_PROJECT_ROOT` | No | current working directory | Project root used by safe command tools. Commands run with fixed executable/argument lists under this directory. |
| `ENABLE_PLUGINS` | No | `false` | If true, enables plugin manifest discovery from `PLUGIN_DIRECTORY`. This phase scans manifests only and does not load plugin assemblies. Use `/plugins` for read-only manifest diagnostics. |
| `PLUGIN_DIRECTORY` | No | `<current working directory>/plugins` | Directory scanned by `/plugins` for plugin folders containing `plugin.json`. Plugin assemblies are trusted OS-level code and should only come from trusted sources before loading is enabled. |
| `ENABLE_GITHUB_TOOLS` | No | `false` | If true, registers read-only GitHub tools such as `github_repo_info`. Keep false unless you want the model to query GitHub. |
| `GITHUB_TOKEN` | No | empty | Optional token for GitHub API requests. Never log or paste it into chat. `/status` only reports configured/not configured. |
| `GITHUB_DEFAULT_OWNER` | No | empty | Default owner used by `github_repo_info` when the tool input is empty. |
| `GITHUB_DEFAULT_REPO` | No | empty | Default repo used by `github_repo_info` when the tool input is empty. |
| `GITHUB_ALLOWED_REPOS` | No | default repo if configured | Comma-separated `owner/repo` allowlist for GitHub tools. Repositories outside this list are rejected. |
| `TELEGRAM_DB_CONNECTION` | No | LocalDB connection | SQL Server connection string |
| `APPLY_MIGRATIONS` | No | `true` | Apply EF migrations on startup |
| `LOG_MESSAGE_CONTENT` | No | `false` | Log user messages and assistant responses. Keep disabled for privacy. |
| `CONVERSATION_MAX_HISTORY` | No | `8` | Number of recent persisted chat messages included in normal agent context. Values are clamped from `1` to `50`. |

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
export ENABLE_AUDIO_TRANSCRIPTION='false'
export ENABLE_SAFE_COMMAND_TOOLS='false'
export SAFE_COMMAND_PROJECT_ROOT='/c/temp/TelegramMessagingTool'
export ENABLE_PLUGINS='false'
export PLUGIN_DIRECTORY='/c/temp/TelegramMessagingTool/plugins'
export ENABLE_GITHUB_TOOLS='false'
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

The repository includes a small dependency-free console test project for helper logic.

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
| `git_status` | No | Optional. Registered only when `ENABLE_SAFE_COMMAND_TOOLS=true`. Read-only `git status --short --branch` for `SAFE_COMMAND_PROJECT_ROOT` |
| `git_diff` | No | Optional. Registered only when `ENABLE_SAFE_COMMAND_TOOLS=true`. Read-only `git diff -- .` for `SAFE_COMMAND_PROJECT_ROOT` |
| `git_log_recent` | No | Optional. Registered only when `ENABLE_SAFE_COMMAND_TOOLS=true`. Read-only `git log --oneline -5` for `SAFE_COMMAND_PROJECT_ROOT` |

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
- `github_repo_info` and `github_list_issues` are read-only and reject repositories outside `GITHUB_ALLOWED_REPOS`.
- `github_list_issues` accepts optional JSON like `{ "owner": "mujahedgt", "repo": "TelegramMessagingTool", "state": "open", "limit": 10 }`; state must be `open`, `closed`, or `all`, and limit is clamped to `1..50`.
- `GITHUB_TOKEN` is optional for read-only requests and is never rendered in tool output, `/status`, or docs examples.

Multi-step tool loop notes:

- The agent can chain safe tools for up to three tool observations before it must produce a final answer.
- After each safe tool result, the model receives a structured observation and can either request one more safe tool or answer.
- If the model keeps requesting tools after the limit, the bot stops and asks the user to narrow or continue with a smaller task.

Safe command tool notes:

- `ENABLE_SAFE_COMMAND_TOOLS=false` by default.
- The first safe command tools are read-only Git inspection tools only: `git_status`, `git_diff`, and `git_log_recent`.
- These tools use fixed executable/argument lists and do not expose arbitrary shell, PowerShell, cmd, bash, file write, delete, commit, push, release, or restart access.

Risky tools such as shell, file write/delete, database mutation, outbound messaging, release, restart, commit, push, or process control are intentionally not included as model tools yet. Use the approval flow before adding dangerous tools.

## P2 image and voice harness planning

The `/harnesses` command shows the next planned agent harnesses before their tools are executable:

| Harness | Status | Planned tool examples |
|---|---|---|
| `image_agent` | planned | `describe_image`, `extract_image_text`, `generate_image_prompt`, `create_image` |
| `voice_agent` | planned | `transcribe_audio`, `summarize_audio`, `extract_audio_tasks`, `speak_text` |

These harnesses are currently **planning only**. The first image-agent foundation is implemented: `/images` lists sandboxed `.png/.jpg/.jpeg/.webp/.gif` files that were uploaded as Telegram documents, and `/describeimage <id>` returns safe metadata plus the configured image model route by default. When `ENABLE_IMAGE_VISION=true`, `/describeimage <id>` sends only that selected sandboxed image to the configured local Ollama image route for a concise description. The first voice-agent foundation is also implemented: `/voicefiles` lists sandboxed `.mp3/.wav/.m4a/.ogg/.oga/.opus/.flac` audio files, `/transcribe <audio-id>` returns safe metadata and a disabled/provider-not-configured message, and `/readfile <audio-id>` returns a safe transcription-not-implemented message. OCR/image generation and real voice/audio transcription/TTS tools are still planned next and should remain read-only/sandboxed first, followed by optional providers behind explicit feature flags.

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

The bot now includes a database-backed approval foundation for future risky actions. This does not execute dangerous tools yet; it stores and tracks approval decisions so future tools can be gated safely.

Commands:

| Command | Purpose |
|---|---|
| `/pending` | Admin-only: list pending actions waiting for your approval |
| `/action <id>` | Admin-only: show audit details for a pending, approved, denied, expired, or executed action |
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
