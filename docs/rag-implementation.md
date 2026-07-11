# RAG Implementation Documentation

This document explains how Retrieval-Augmented Generation (RAG) is implemented in TelegramMessagingTool, how the current retrieval pipeline works, and what a stronger production-grade approach would look like.

## 1. What RAG means in this project

RAG lets the bot answer questions from the user's saved documents instead of relying only on the base Ollama model memory.

In this project, RAG is scoped to the current Telegram user/chat:

1. A user uploads or imports a supported document into the sandbox.
2. The bot extracts text from the file.
3. The extracted text is split into smaller chunks.
4. Chunks are stored in SQL Server as `DocumentChunk` records.
5. Optional embeddings are generated with Ollama and stored in SQL and/or an external vector store.
6. `/askfile` or `/askdocs` retrieves the most relevant chunks.
7. The selected chunks are inserted into a strict document-QA prompt.
8. Ollama answers using only those excerpts and includes citations.

## 2. Main files and responsibilities

| Area | Main files | Responsibility |
|---|---|---|
| File sandbox and extraction | `Services/DocumentStorageService.cs` | Saves files under `UserFiles/<chatId>/`, validates paths/extensions/size, extracts text from TXT/MD/JSON/CSV/PDF/DOCX/XLSX, returns image/audio metadata messages instead of decoding binary media as text. |
| Chunking | `Models/DocumentChunk.cs`, `Services/DocumentChunker.cs` | Splits extracted document text into bounded overlapping chunks. |
| Indexing | `Services/DocumentIndexingService.cs` | Implements `/indexfile` and `/indexdocs` behavior by extracting text, deleting old chunks for the file, and inserting fresh `DocumentChunk` rows. |
| Retrieval | `Services/DocumentRetrievalService.cs` | Searches indexed chunks using vector store first, then SQL embedding/hybrid scoring, then lexical scoring fallback. |
| Answer generation | `Services/DocumentQuestionAnsweringService.cs` | Builds the final prompt and calls Ollama with `ModelTaskKind.DocumentQuestionAnswering`. |
| Summaries | `Services/DocumentSummaryService.cs` | Builds summary prompts for `/summarizefile` and `/summarizedocs`. |
| Embeddings | `Services/DocumentEmbeddingService.cs`, `Services/OllamaEmbeddingClient.cs` | Calls Ollama `/api/embed` with `OLLAMA_EMBEDDING_MODEL` and serializes embeddings. |
| Vector abstraction | `Services/Vector/*` | Provides `IVectorStore`, local JSON vector store, Qdrant vector store, vector factory, and maintenance commands. |
| Commands | `Commands/*Document*`, vector commands | User/admin entry points such as `/indexfile`, `/askdocs`, `/embedfile`, `/vectorstatus`, `/vectorsync`, `/vectorrepair`. |

## 3. Supported document inputs

The sandbox supports these document/text extraction types:

- `.txt`
- `.md`
- `.json`
- `.csv`
- `.pdf`
- `.docx`
- `.xlsx`

Image and audio files are allowed as saved artifacts for image/voice workflows, but they are not treated as normal text documents by `ExtractTextAsync`:

- Image files return a message pointing to image-agent/OCR commands.
- Audio files return a message pointing to voice/transcription commands.

This prevents accidental binary decoding and keeps RAG focused on text-bearing documents or generated transcript/OCR text files.

## 4. Indexing flow

### 4.1 Upload/import

Files are saved through `DocumentStorageService` under a per-chat directory:

```text
UserFiles/<chatId>/<timestamp>-<guid>-<safe-file-name>
```

Safety checks include:

- Filename sanitization with `Path.GetFileName`.
- Extension allowlist.
- Maximum file size checks.
- Root-directory containment check to block traversal.
- Stable project-root storage so timestamped releases do not lose document state.

### 4.2 Text extraction

`DocumentStorageService.ExtractTextAsync(...)` handles extraction:

| Type | Extraction path |
|---|---|
| TXT/MD/JSON/CSV | UTF-8 text read with bounded character count. |
| PDF | `UglyToad.PdfPig` page text extraction. |
| DOCX | OpenXML paragraph/text node extraction. |
| XLSX | OpenXML sheet/row/cell text extraction. |

Recent hardening added bounded extraction behavior so large text/PDF/DOCX/XLSX content is not fully accumulated before truncation.

### 4.3 Chunk creation

`DocumentIndexingService.IndexFileAsync(...)` extracts up to a bounded amount of text and passes it into `DocumentChunker.Split(...)`.

Each chunk is stored with metadata:

- `UploadedFileId`
- `ConnectedUserId`
- `ChatId`
- `ChunkNumber`
- `OriginalFileName`
- `Text`
- `CharacterCount`
- optional `EmbeddingJson`

Old chunks for the same file/user are removed before inserting the new index.

## 5. Retrieval flow

`DocumentRetrievalService.SearchAsync(...)` is the core retrieval method.

It first loads up to the latest 1000 chunks for the current user, optionally filtered to one file.

Then it tries retrieval in this order:

### 5.1 External/configured vector store first

If both an embedding service and an `IVectorStore` are available:

1. Embed the user's question.
2. Search the configured vector store by chat ID.
3. Map returned vector chunk IDs back to SQL `DocumentChunk` rows.
4. Return the top matching chunks.

This path is used when `VECTOR_STORE_PROVIDER` is `local_json` or `qdrant` and embeddings are enabled.

### 5.2 SQL-stored embedding fallback

If the vector store is missing or fails, but chunks have `EmbeddingJson`:

1. Embed the question.
2. Parse stored chunk embeddings.
3. Score chunks with hybrid scoring:
   - semantic cosine similarity
   - lexical term score
4. Return highest-ranked chunks.

### 5.3 Lexical fallback

If embedding/vector retrieval is unavailable or fails, retrieval falls back to lexical scoring.

Lexical scoring:

- Tokenizes the question.
- Removes common stop words.
- Scores chunks by term frequency and exact phrase/term matches.
- Orders by score, file ID, and chunk number.

This fail-safe behavior is important: document Q&A should still work even if Ollama embeddings, Qdrant, or local vector JSON fail.

## 6. Answer generation flow

`DocumentQuestionAnsweringService.BuildPrompt(...)` creates a strict grounded prompt:

- The model is told it is answering about saved documents.
- It must use only the provided excerpts.
- If the answer is absent, it must say:

```text
I could not find that in the indexed document text.
```

- Each excerpt is labelled with:

```text
[File #<id> <filename>, chunk <chunk-number>]
```

- The model is instructed to answer with citations.

The request is routed with:

```csharp
ModelTaskKind.DocumentQuestionAnswering
```

That means `OLLAMA_MODEL_DOC_QA` can be configured separately from normal chat.

## 7. Embeddings and vector stores

### 7.1 Embedding model

Embeddings use Ollama `/api/embed`.

Important config:

| Variable | Purpose |
|---|---|
| `ENABLE_DOCUMENT_EMBEDDINGS` | Enables semantic retrieval paths. |
| `OLLAMA_EMBEDDING_URL` | Defaults from `OLLAMA_URL` as `/api/embed`. |
| `OLLAMA_EMBEDDING_MODEL` | Defaults to `nomic-embed-text`. |

### 7.2 Vector provider modes

| Provider | Behavior |
|---|---|
| `embedding_json` | Default. Stores embeddings in SQL `DocumentChunk.EmbeddingJson`; no external vector store. |
| `local_json` | Mirrors vectors into a local JSON file through `LocalJsonVectorStore`. Useful for local prototype/testing. |
| `qdrant` | Stores/searches vectors through Qdrant HTTP API. Better for larger collections. |

### 7.3 Local JSON hardening

The local JSON vector store is still a local fallback, but it now has stronger durability behavior:

- Per-file async lock to avoid concurrent write corruption.
- Atomic replace-style writes through temporary files.
- Clear corruption errors instead of silently wiping invalid JSON.
- Delete-by-uploaded-file support for vector maintenance.

## 8. Vector maintenance commands

| Command | Purpose |
|---|---|
| `/vectorstatus` | Shows current vector provider/path/config state. |
| `/vectorsync` | Syncs indexed chunks into the configured vector store. |
| `/vectorclear` | Clears vector entries for saved/indexed files where supported. |
| `/vectorrepair` | Rebuilds vector state from indexed chunks. |
| `/embedfile <id>` | Generates embeddings for one indexed file. |
| `/embeddocs` | Generates embeddings for all indexed documents. |
| `/reembeddocs` | Regenerates embeddings. |

## 9. Current strengths

- **Safe-by-default fallback:** lexical retrieval works without embeddings.
- **Per-user isolation:** retrieval is scoped by `ConnectedUserId`/`ChatId`.
- **Sandboxed files:** no arbitrary filesystem reads.
- **Citations:** prompt labels source file/chunk metadata.
- **Model routing:** document Q&A can use a dedicated Ollama route.
- **Vector abstraction:** local JSON and Qdrant can be swapped through config.
- **Failure resilience:** vector/embedding failures fall back to SQL/lexical search.

## 10. Current limitations

| Limitation | Impact |
|---|---|
| Lexical scoring is simple | It can miss paraphrased questions when embeddings are disabled. |
| Chunking is generic | It does not yet preserve sections/tables/headings as strongly as specialized parsers. |
| Context packing is basic | It selects top chunks but does not yet optimize for diversity, neighboring chunks, or token budget. |
| No reranker | Similar chunks may be included while a better supporting chunk is missed. |
| Local JSON vector store is prototype-level | Good for local use, but Qdrant or another vector DB is better for scale. |
| Citations depend on prompt compliance | The model is instructed to cite, but there is no post-generation citation verifier yet. |
| No OCR/scanned-PDF RAG by default | Scanned documents need OCR first, then indexing of OCR output. |

## 11. Better approach for future versions

A stronger RAG system would keep this safe foundation but improve retrieval quality and reliability.

### Recommended next architecture

1. **Use Qdrant as the main vector store**
   - Keep SQL as metadata/source of truth.
   - Store vectors in Qdrant with payload fields: user ID, chat ID, file ID, chunk ID, filename, chunk number.
   - Use Qdrant filters for per-user isolation.

2. **Improve chunking**
   - Use structure-aware chunking for PDFs/DOCX:
     - headings
     - sections
     - tables
     - page numbers
   - Store page/section metadata for better citations.

3. **Hybrid retrieval with reranking**
   - First-stage retrieval:
     - BM25/lexical top N
     - vector top N
   - Merge candidates.
   - Rerank with a local cross-encoder/reranker or small LLM scoring prompt.
   - Send only the best diverse chunks to the answer model.

4. **Context packing**
   - Add neighboring chunks when one chunk scores highly.
   - Avoid sending near-duplicates.
   - Fit chunks to a token budget instead of only a chunk count.

5. **Citation verification**
   - Parse answer citations.
   - Verify cited file/chunk IDs were actually in the provided context.
   - If not, append a warning or regenerate.

6. **Incremental indexing pipeline**
   - Store document hash and parser version.
   - Re-index only when file content or parser version changes.
   - Add background indexing for large imports.

7. **OCR pipeline for scanned PDFs/images**
   - OCR scanned PDF pages/images into text artifacts.
   - Index the OCR text with source references to page/image IDs.

8. **Evaluation set**
   - Keep a small fixture set of documents and questions.
   - Test exact answerability, absent-answer behavior, citation correctness, and retrieval hit rate.

## 12. Practical recommendation

For Mujahed's local Telegram agent, the best next practical step is:

1. Keep the current SQL + lexical fallback path.
2. Use `nomic-embed-text` embeddings.
3. Prefer `VECTOR_STORE_PROVIDER=qdrant` once document volume grows beyond small local experiments.
4. Add a retrieval evaluation harness before changing ranking logic.
5. Then implement hybrid retrieval + reranking.

That gives the highest quality improvement without weakening the current safety model.

## 13. Quick setup example

```bash
export ENABLE_DOCUMENT_EMBEDDINGS='true'
export OLLAMA_EMBEDDING_MODEL='nomic-embed-text'
export VECTOR_STORE_PROVIDER='qdrant'
export QDRANT_URL='http://localhost:6333'
export QDRANT_COLLECTION='telegram_documents'
```

Then in Telegram:

```text
/importfiles
/importfile my-document.pdf
/indexfile <file-id>
/embedfile <file-id>
/askfile <file-id> What is the payment deadline?
```

For all documents:

```text
/indexdocs
/embeddocs
/askdocs What are the main risks mentioned in my documents?
```
