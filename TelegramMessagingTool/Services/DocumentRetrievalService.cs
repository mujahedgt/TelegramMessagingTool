using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class DocumentRetrievalService
{
    private readonly ITextEmbeddingService? _embeddingService;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "how", "i", "in",
        "is", "it", "me", "my", "of", "on", "or", "that", "the", "this", "to", "what", "when",
        "where", "which", "who", "why", "with", "you", "do", "does", "did", "have", "has"
    };

    public DocumentRetrievalService(ITextEmbeddingService? embeddingService = null)
    {
        _embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<DocumentChunk>> SearchAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        string question,
        int? fileId,
        int limit,
        CancellationToken cancellationToken)
    {
        IQueryable<DocumentChunk> query = dbContext.DocumentChunks
            .Where(x => x.ConnectedUserId == user.Id);

        if (fileId.HasValue)
        {
            query = query.Where(x => x.UploadedFileId == fileId.Value);
        }

        List<DocumentChunk> chunks = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(1000)
            .ToListAsync(cancellationToken);

        if (_embeddingService is not null && chunks.Any(x => !string.IsNullOrWhiteSpace(x.EmbeddingJson)))
        {
            try
            {
                IReadOnlyList<float> questionEmbedding = await _embeddingService.EmbedAsync(question, cancellationToken);
                IReadOnlyList<DocumentChunk> hybridChunks = RankChunksByHybridScore(chunks, question, questionEmbedding, limit);
                if (hybridChunks.Count > 0)
                {
                    return hybridChunks;
                }
            }
            catch (Exception)
            {
                // Keep Q&A reliable: embedding errors fall back to local lexical search.
            }
        }

        return RankChunks(chunks, question, limit);
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetSummaryChunksAsync(
        TelegramDbContext dbContext,
        ConnectedUser user,
        int? fileId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        IQueryable<DocumentChunk> query = dbContext.DocumentChunks
            .Where(x => x.ConnectedUserId == user.Id);

        if (fileId.HasValue)
        {
            query = query.Where(x => x.UploadedFileId == fileId.Value);
        }

        List<DocumentChunk> chunks = await query
            .OrderBy(x => x.UploadedFileId)
            .ThenBy(x => x.ChunkNumber)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return chunks;
    }

    public static IReadOnlyList<DocumentChunk> RankChunks(
        IEnumerable<DocumentChunk> chunks,
        string question,
        int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        List<string> terms = Tokenize(question).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (terms.Count == 0)
        {
            return chunks.Take(limit).ToList();
        }

        return chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = Score(chunk.Text, terms)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.UploadedFileId)
            .ThenBy(x => x.Chunk.ChunkNumber)
            .Take(limit)
            .Select(x => x.Chunk)
            .ToList();
    }

    public static IReadOnlyList<DocumentChunk> RankChunksByHybridScore(
        IEnumerable<DocumentChunk> chunks,
        string question,
        IReadOnlyList<float> questionEmbedding,
        int limit)
    {
        if (limit <= 0 || questionEmbedding.Count == 0)
        {
            return [];
        }

        List<string> terms = Tokenize(question).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return chunks
            .Select(chunk =>
            {
                IReadOnlyList<float> chunkEmbedding = EmbeddingMath.Parse(chunk.EmbeddingJson);
                double semanticScore = EmbeddingMath.CosineSimilarity(questionEmbedding, chunkEmbedding);
                int lexicalScore = terms.Count == 0 ? 0 : Score(chunk.Text, terms);
                return new
                {
                    Chunk = chunk,
                    SemanticScore = semanticScore,
                    LexicalScore = lexicalScore,
                    Score = (semanticScore * 100.0) + lexicalScore
                };
            })
            .Where(x => x.SemanticScore > 0 || x.LexicalScore > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.UploadedFileId)
            .ThenBy(x => x.Chunk.ChunkNumber)
            .Take(limit)
            .Select(x => x.Chunk)
            .ToList();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (Match match in Regex.Matches(text.ToLowerInvariant(), "[\\p{L}\\p{N}]{3,}"))
        {
            string term = match.Value;
            if (!StopWords.Contains(term))
            {
                yield return term;
            }
        }
    }

    private static int Score(string chunkText, IReadOnlyList<string> terms)
    {
        string haystack = chunkText.ToLowerInvariant();
        int score = 0;
        int matchedTerms = 0;

        foreach (string term in terms)
        {
            int matches = Regex.Matches(haystack, $"\\b{Regex.Escape(term)}\\b", RegexOptions.IgnoreCase).Count;
            if (matches > 0)
            {
                matchedTerms++;
                score += matches * 4;
            }
        }

        if (matchedTerms > 1)
        {
            score += matchedTerms * matchedTerms;
        }

        for (int i = 0; i < terms.Count - 1; i++)
        {
            string phrase = $"{terms[i]} {terms[i + 1]}";
            if (haystack.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
            }
        }

        string normalizedQuestion = string.Join(' ', terms);
        if (normalizedQuestion.Length > 6 && haystack.Contains(normalizedQuestion, StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        return score;
    }
}
