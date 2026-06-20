# Documentation Q&A Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Add safe document Q&A so a user can upload PDF/DOCX/XLSX/CSV/TXT/MD files and ask questions over their own saved documents from Telegram or the local console.

**Architecture:** Reuse the existing sandboxed `UploadedFile` storage and `DocumentStorageService.ExtractTextAsync(...)`. Add chunking, lightweight retrieval, and Q&A commands first using SQL Server tables; then optionally add local embeddings/reranking later. Keep all document reads inside `UserFiles/<chatId>/` and never allow arbitrary path access.

**Tech Stack:** .NET 10, C#, EF Core SQL Server LocalDB, existing Telegram command router, existing Ollama `AgentRunner`, `DocumentStorageService`, `PdfPig`, `DocumentFormat.OpenXml`.

---

## Current context

The project already has:

- `TelegramMessagingTool/Services/DocumentStorageService.cs`
  - supports `.txt`, `.md`, `.json`, `.csv`, `.pdf`, `.docx`, `.xlsx`
  - extracts text through `ExtractTextAsync(UploadedFile, CancellationToken, int maxCharacters = 12000)`
  - stores files under a per-chat sandbox
- `TelegramMessagingTool/models/UploadedFile.cs`
- commands:
  - `/files`
  - `/readfile <id>`
  - `/createfile <filename> <content>`
- local console access now uses the same command router and agent runner as Telegram

The next step should turn “read the whole file” into “ask a focused question over one or more files”.

---

## Proposed user commands

| Command | Purpose |
|---|---|
| `/askfile <file-id> <question>` | Ask one document a question |
| `/askdocs <question>` | Ask across all saved documents for the current user |
| `/indexfile <file-id>` | Extract and chunk a file into searchable document chunks |
| `/indexdocs` | Index all user files that are not indexed yet |
| `/docchunks <file-id>` | Show chunk/index status for debugging |

Optional later:

| Command | Purpose |
|---|---|
| `/forgetfile <file-id>` | Delete a file and its chunks after approval or confirmation |
| `/summarizefile <file-id>` | Summarize one file |
| `/extracttable <file-id>` | Extract tabular content from CSV/XLSX/PDF/DOCX |

---

## Task 1: Add document chunk model

**Objective:** Store extracted text chunks in the database so Q&A does not re-read huge files every time.

**Files:**
- Create: `TelegramMessagingTool/models/DocumentChunk.cs`
- Modify: `TelegramMessagingTool/Data/DbContext.cs`
- Migration: `TelegramMessagingTool/Migrations/<timestamp>_AddDocumentChunks.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Implementation sketch:**

```csharp
namespace TelegramMessagingTool.Models;

public class DocumentChunk
{
    public int Id { get; set; }
    public int UploadedFileId { get; set; }
    public int ConnectedUserId { get; set; }
    public long ChatId { get; set; }
    public int ChunkNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public UploadedFile UploadedFile { get; set; } = null!;
    public ConnectedUser User { get; set; } = null!;
}
```

**DbContext changes:**

```csharp
public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
```

Add indexes:

```csharp
modelBuilder.Entity<DocumentChunk>()
    .HasIndex(x => new { x.ConnectedUserId, x.UploadedFileId, x.ChunkNumber });
```

**Verification:**

```bash
dotnet ef migrations add AddDocumentChunks --project TelegramMessagingTool/TelegramMessagingTool.csproj
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
```

---

## Task 2: Add deterministic text chunker

**Objective:** Split extracted text into overlapping chunks suitable for local LLM Q&A.

**Files:**
- Create: `TelegramMessagingTool/Services/DocumentChunker.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Rules:**

- default chunk size: `2500` characters
- overlap: `250` characters
- preserve paragraph boundaries where practical
- skip empty chunks
- hard cap per chunk to avoid oversized prompts

**Implementation sketch:**

```csharp
namespace TelegramMessagingTool.Services;

public static class DocumentChunker
{
    public static IReadOnlyList<string> Split(string text, int chunkSize = 2500, int overlap = 250)
    {
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (overlap < 0 || overlap >= chunkSize) throw new ArgumentOutOfRangeException(nameof(overlap));
        if (string.IsNullOrWhiteSpace(text)) return [];

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        List<string> chunks = [];
        int start = 0;
        while (start < normalized.Length)
        {
            int length = Math.Min(chunkSize, normalized.Length - start);
            string chunk = normalized.Substring(start, length).Trim();
            if (!string.IsNullOrWhiteSpace(chunk)) chunks.Add(chunk);
            if (start + length >= normalized.Length) break;
            start += chunkSize - overlap;
        }

        return chunks;
    }
}
```

**Tests:**

- short text returns one chunk
- long text returns multiple chunks
- each chunk is at most chunk size
- overlap keeps continuity
- blank text returns no chunks

---

## Task 3: Add document indexing service

**Objective:** Extract a user file, chunk it, replace old chunks, and save new chunks.

**Files:**
- Create: `TelegramMessagingTool/Services/DocumentIndexingService.cs`
- Modify tests: `TelegramMessagingTool.Tests/Program.cs`

**Service shape:**

```csharp
public sealed class DocumentIndexingService
{
    private readonly DocumentStorageService _documentStorage;

    public DocumentIndexingService(DocumentStorageService documentStorage)
    {
        _documentStorage = documentStorage;
    }

    public Task<int> IndexFileAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int fileId,
        CancellationToken cancellationToken);
}
```

**Important rules:**

- only index files where `UploadedFile.ConnectedUserId == user.Id`
- call `ExtractTextAsync(..., maxCharacters: 200000)` for indexing
- delete existing chunks for that file before adding new chunks
- if extracted text is empty, return `0` and explain through command
- do not index unsupported file types

---

## Task 4: Add `/indexfile` and `/indexdocs`

**Objective:** Let user manually prepare document chunks before asking questions.

**Files:**
- Create: `TelegramMessagingTool/Commands/IndexFileCommand.cs`
- Create: `TelegramMessagingTool/Commands/IndexDocsCommand.cs`
- Modify: `TelegramMessagingTool/Program.cs`
- Modify: `TelegramMessagingTool/Commands/HelpCommand.cs`
- Modify: `README.md`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Expected replies:**

```text
Indexed file #3: manual.pdf
Chunks created: 12
```

```text
Indexed 4 files.
Chunks created: 37
Skipped: 1 empty/unsupported file
```

---

## Task 5: Add simple lexical retrieval service

**Objective:** Find relevant chunks without embeddings first, so the feature works fully local and simple.

**Files:**
- Create: `TelegramMessagingTool/Services/DocumentRetrievalService.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Approach:**

- tokenize question into lowercase words
- remove tiny/common stop words
- score chunks by term frequency and phrase hits
- prefer chunks from the target file for `/askfile`
- return top 4 chunks

**Service shape:**

```csharp
public sealed class DocumentRetrievalService
{
    public Task<IReadOnlyList<DocumentChunk>> SearchAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        string question,
        int? fileId,
        int limit,
        CancellationToken cancellationToken);
}
```

This is not as good as embeddings, but it is reliable, transparent, and easy to test. Add embeddings later using `bge-m3` or `nomic-embed-text-v1.5`.

---

## Task 6: Add document Q&A prompt builder

**Objective:** Build a strict prompt that answers only from retrieved chunks.

**Files:**
- Create: `TelegramMessagingTool/Services/DocumentQuestionAnsweringService.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Rules for the model:**

- answer only from provided chunks
- cite file ID, filename, and chunk number
- if answer is not in chunks, say it is not found
- do not invent facts
- mention scanned PDFs if extracted text is empty

**Prompt shape:**

```text
You are answering a question about the user's saved documents.
Use ONLY the document excerpts below.
If the answer is not present, say: "I could not find that in the indexed document text."

Question:
{question}

Document excerpts:
[File #3 manual.pdf, chunk 2]
...

Answer with citations.
```

---

## Task 7: Add `/askfile` command

**Objective:** Ask one indexed document a question.

**Files:**
- Create: `TelegramMessagingTool/Commands/AskFileCommand.cs`
- Modify: `TelegramMessagingTool/Program.cs`
- Modify: `HelpCommand.cs`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Behavior:**

1. Parse `/askfile <file-id> <question>`.
2. Verify the file belongs to the user.
3. If no chunks exist for the file, auto-index it once.
4. Retrieve top chunks for that file.
5. Ask Ollama with Q&A prompt.
6. Return answer with citations.

**Example:**

```text
/askfile 3 what is the payment deadline?
```

---

## Task 8: Add `/askdocs` command

**Objective:** Ask across all indexed documents for the current user.

**Files:**
- Create: `TelegramMessagingTool/Commands/AskDocsCommand.cs`
- Modify: `TelegramMessagingTool/Program.cs`
- Modify: `README.md`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Behavior:**

1. Parse `/askdocs <question>`.
2. If the user has no chunks, tell them to upload/index files or run `/indexdocs`.
3. Retrieve top chunks across files.
4. Ask Ollama with the Q&A prompt.
5. Return answer with citations.

---

## Task 9: Add `/docchunks` debug/status command

**Objective:** Show index status for a file so the user understands what is searchable.

**Files:**
- Create: `TelegramMessagingTool/Commands/DocChunksCommand.cs`
- Modify: `Program.cs`, `HelpCommand.cs`, `README.md`
- Test: `TelegramMessagingTool.Tests/Program.cs`

**Example reply:**

```text
File #3: manual.pdf
Chunks: 12
Total indexed characters: 28,430
Last indexed: 2026-06-21 00:05 UTC
```

---

## Task 10: Verification and release

**Objective:** Verify, publish, push, and hand off runtime safely.

Run:

```bash
cd /c/temp/TelegramMessagingTool
dotnet run --project TelegramMessagingTool.Tests/TelegramMessagingTool.Tests.csproj --configuration Release --nologo
dotnet build TelegramMessagingTool.slnx --configuration Release --nologo
dotnet list TelegramMessagingTool/TelegramMessagingTool.csproj package --vulnerable --include-transitive
```

Publish:

```bash
release_dir="release/TelegramMessagingTool-$(date +%Y%m%d-%H%M%S)"
dotnet publish TelegramMessagingTool/TelegramMessagingTool.csproj --configuration Release --output "$release_dir" --nologo
printf '%s' "$release_dir" > .latest-release
```

Commit:

```bash
git add TelegramMessagingTool TelegramMessagingTool.Tests README.md .latest-release .hermes/plans
git commit -m "Add document Q&A planning"
git push origin master
```

Runtime:

- If a token-bearing runtime is already running, do not kill it unless the current shell can restart with `TELEGRAM_BOT_TOKEN`.
- If restart is safe, run the latest-release startup launcher or published exe.

---

## Risks and tradeoffs

- **Scanned PDFs:** PdfPig cannot extract image-only PDFs. OCR should be a later phase using Tesseract or a vision model.
- **Lexical retrieval quality:** Simple keyword scoring is good enough for v1 but embeddings will be better.
- **Prompt size:** Limit top chunks and chunk size to avoid huge Ollama prompts.
- **Privacy:** Keep all chunks per-user and never search another user's chunks.
- **Excel complexity:** `.xlsx` extraction currently reads visible cell values; formulas, formatting, and multiple sheets may need richer metadata later.

---

## Future embedding upgrade

After v1 lexical Q&A works, add:

- local embedding model: `BAAI/bge-m3` or `nomic-embed-text-v1.5`
- vector storage: SQLite vector extension, Qdrant, or a simple persisted embedding table
- optional reranker: `bge-reranker-v2-m3`

Keep the lexical retriever as a fallback if the embedding service is unavailable.
