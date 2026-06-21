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
- Built-in tools: `datetime`, `calculator`, `status`, and `online_search`
- `/tools` command to show available tools
- Agent-style startup console panel with commands, model, safety, and tool status
- Local console chat/command input using the same command router, memory, tools, and agent runner as Telegram
- Live console event lines for startup, commands, messages, denied users, shutdown, and errors
- Sandboxed document/file support for `.txt`, `.md`, `.json`, `.csv`, `.pdf`, `.docx`, and `.xlsx`
- File commands: `/files`, `/readfile <id>`, and `/createfile <filename> <content>`
- Document Q&A indexing, question, summary, and embedding commands: `/indexfile`, `/indexdocs`, `/docchunks`, `/askfile`, `/askdocs`, `/summarizefile`, `/summarizedocs`, `/embedfile`, and `/embeddocs`
- Read-only local device commands: `/systeminfo`, `/diskstatus`, and `/processes [count]`
- Safe approval foundation for future risky tools
- Approval commands: `/pending`, `/approve <id>`, and `/deny <id>`
- Task planner commands: `/plan <goal>`, `/tasks`, `/task <id>`, `/done <task-id> [step-number]`, and `/cancel <task-id>`

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
| `ALLOWED_CHAT_IDS` | No | empty | Comma-separated Telegram chat IDs allowed to use the bot. Empty means allow all. |
| `OLLAMA_URL` | No | `http://localhost:11434/api/chat` | Ollama chat API endpoint |
| `OLLAMA_MODEL` | No | `llama3.2:3b` | Ollama model name |
| `OLLAMA_EMBEDDING_URL` | No | derived from `OLLAMA_URL` as `/api/embed` | Ollama embedding API endpoint |
| `OLLAMA_EMBEDDING_MODEL` | No | `nomic-embed-text` | Local embedding model used by `/embedfile` and `/embeddocs` |
| `ENABLE_DOCUMENT_EMBEDDINGS` | No | `false` | If true, `/askfile` and `/askdocs` use stored embeddings for hybrid semantic retrieval when available |
| `TELEGRAM_DB_CONNECTION` | No | LocalDB connection | SQL Server connection string |
| `APPLY_MIGRATIONS` | No | `true` | Apply EF migrations on startup |
| `LOG_MESSAGE_CONTENT` | No | `false` | Log user messages and assistant responses. Keep disabled for privacy. |

Example Git Bash setup:

```bash
export TELEGRAM_BOT_TOKEN='123456:your-token'
export ADMIN_CHAT_ID='123456789'
export ALLOWED_CHAT_IDS='123456789,987654321'
export OLLAMA_MODEL='llama3.2:3b'
export OLLAMA_EMBEDDING_MODEL='nomic-embed-text'
export ENABLE_DOCUMENT_EMBEDDINGS='false'
export LOG_MESSAGE_CONTENT='false'
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
| `online_search` | No | Public web search through DuckDuckGo Lite, Startpage, and Mojeek fallbacks; corrects common obvious query typos and expands vehicle searches with price/spec terms |

Search behavior notes:

- The model is instructed to use `online_search` for current facts, prices, market values, specs, products, cars, and news.
- The bot now hides raw `tool_call` JSON from the final answer after the tool runs.
- For clear misspellings such as `Mitsubateie Lanser 1992`, the search tool tries corrected/expanded variants such as `Mitsubishi Lancer 1992 price specs review`.
- Final search answers should summarize only what the returned search results support and include useful source links.

Multi-step tool loop notes:

- The agent can chain safe tools for up to three tool observations before it must produce a final answer.
- After each safe tool result, the model receives a structured observation and can either request one more safe tool or answer.
- If the model keeps requesting tools after the limit, the bot stops and asks the user to narrow or continue with a smaller task.

Risky tools such as shell, file write/delete, database mutation, or outbound messaging are intentionally not included yet. Use the approval flow before adding dangerous tools.

## Read-only local device commands

These commands inspect the local machine without changing anything:

| Command | Purpose |
|---|---|
| `/systeminfo` | Show operating system, architecture, machine name, CPU count, .NET runtime, uptime, and process memory |
| `/diskstatus` | Show ready local drives, total space, free space, and used percentage |
| `/processes [count]` | Show running process count and top processes by memory usage; count is clamped to a safe range |
| `/killprocess <pid>` | Create a high-risk pending approval request to terminate a process later; does not execute immediately |

These commands are intentionally **read-only**. They do not stop processes, delete files, edit files, or run arbitrary shell commands.

`/killprocess <pid>` is the first risky local-control request command. It only creates a pending approval request with risk level `high`; this version still does **not** terminate the process. Actual approved-action execution is intentionally deferred to a later patch.

## Approval flow

The bot now includes a database-backed approval foundation for future risky actions. This does not execute dangerous tools yet; it stores and tracks approval decisions so future tools can be gated safely.

Commands:

| Command | Purpose |
|---|---|
| `/pending` | List pending actions waiting for your approval |
| `/approve <id>` | Approve a pending action |
| `/deny <id>` | Deny a pending action |

Pending actions expire automatically if they are not approved before their expiry time.

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

## Console UI

On startup the console shows a readable agent dashboard:

- runtime status: bot username, model, Ollama endpoint, database summary, migrations, allowlist, logging
- command list in compact columns
- registered agent tools
- quick-start examples that work from either Telegram or the local console
- safety warnings when `ALLOWED_CHAT_IDS` is missing or message-content logging is enabled
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
- If `ALLOWED_CHAT_IDS` is empty, any Telegram user who finds the bot can use it. Set an allowlist before real use.
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

1. Set `ALLOWED_CHAT_IDS` in production.
2. Keep `LOG_MESSAGE_CONTENT=false` unless actively debugging.
3. Store secrets in environment variables or a secret manager, not in source files.
4. Use a production database connection string outside local development.
5. Review published logs before sharing archives.
6. Keep `UserFiles/` private because it contains user-uploaded and bot-created documents.
