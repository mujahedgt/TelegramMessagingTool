namespace TelegramMessagingTool.Services;

public static class DocumentChunker
{
    public const int DefaultChunkSize = 2500;
    public const int DefaultOverlap = 250;

    public static IReadOnlyList<string> Split(
        string text,
        int chunkSize = DefaultChunkSize,
        int overlap = DefaultOverlap)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
        }

        if (overlap < 0 || overlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "Overlap must be non-negative and smaller than chunk size.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        string normalized = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();

        List<string> chunks = [];
        int start = 0;
        int step = chunkSize - overlap;
        while (start < normalized.Length)
        {
            int length = Math.Min(chunkSize, normalized.Length - start);
            int end = start + length;

            if (end < normalized.Length)
            {
                int paragraphBreak = normalized.LastIndexOf("\n\n", end - 1, length, StringComparison.Ordinal);
                if (paragraphBreak > start + chunkSize / 2)
                {
                    end = paragraphBreak;
                    length = end - start;
                }
            }

            string chunk = normalized.Substring(start, length).Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk.Length <= chunkSize ? chunk : chunk[..chunkSize]);
            }

            if (end >= normalized.Length)
            {
                break;
            }

            start = Math.Max(end - overlap, start + step);
        }

        return chunks;
    }
}
