using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Data;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Services;

public sealed class DocumentRetrievalService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "how", "i", "in",
        "is", "it", "me", "my", "of", "on", "or", "that", "the", "this", "to", "what", "when",
        "where", "which", "who", "why", "with", "you", "do", "does", "did", "have", "has"
    };

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

        return RankChunks(chunks, question, limit);
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

        List<string> terms = Tokenize(question).ToList();
        if (terms.Count == 0)
        {
            return chunks.Take(limit).ToList();
        }

        return chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = Score(chunk.Text, terms, question)
            })
            .Where(x => x.Score > 0)
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

    private static int Score(string chunkText, IReadOnlyList<string> terms, string question)
    {
        string haystack = chunkText.ToLowerInvariant();
        int score = 0;
        foreach (string term in terms)
        {
            score += Regex.Matches(haystack, Regex.Escape(term), RegexOptions.IgnoreCase).Count * 3;
        }

        string normalizedQuestion = string.Join(' ', terms);
        if (normalizedQuestion.Length > 6 && haystack.Contains(normalizedQuestion, StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }
}
