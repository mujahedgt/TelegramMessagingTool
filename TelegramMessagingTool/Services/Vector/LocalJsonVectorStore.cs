using System.Text.Json;

namespace TelegramMessagingTool.Services.Vector;

public sealed class LocalJsonVectorStore : IVectorStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public LocalJsonVectorStore(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Vector store path is required.", nameof(path))
            : path;
    }

    public async Task UpsertAsync(DocumentVector vector, CancellationToken cancellationToken)
    {
        List<DocumentVector> vectors = await ReadAllAsync(cancellationToken);
        int existingIndex = vectors.FindIndex(x => string.Equals(x.Id, vector.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            vectors[existingIndex] = vector;
        }
        else
        {
            vectors.Add(vector);
        }

        await WriteAllAsync(vectors, cancellationToken);
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        long chatId,
        IReadOnlyList<float> queryEmbedding,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0 || queryEmbedding.Count == 0)
        {
            return [];
        }

        List<DocumentVector> vectors = await ReadAllAsync(cancellationToken);
        return vectors
            .Where(x => x.ChatId == chatId && x.Embedding.Count > 0)
            .Select(x => new VectorSearchResult(x, CosineSimilarity(queryEmbedding, x.Embedding)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Vector.UploadedFileId)
            .ThenBy(x => x.Vector.ChunkNumber)
            .Take(limit)
            .ToList();
    }

    public async Task DeleteByUploadedFileIdAsync(int uploadedFileId, CancellationToken cancellationToken)
    {
        List<DocumentVector> vectors = await ReadAllAsync(cancellationToken);
        int removed = vectors.RemoveAll(x => x.UploadedFileId == uploadedFileId);
        if (removed > 0)
        {
            await WriteAllAsync(vectors, cancellationToken);
        }
    }

    private async Task<List<DocumentVector>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using FileStream stream = File.OpenRead(_path);
        List<DocumentVector>? vectors = await JsonSerializer.DeserializeAsync<List<DocumentVector>>(stream, JsonOptions, cancellationToken);
        return vectors ?? [];
    }

    private async Task WriteAllAsync(List<DocumentVector> vectors, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _path + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, vectors, JsonOptions, cancellationToken);
        }

        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        File.Move(temporaryPath, _path);
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        int length = Math.Min(left.Count, right.Count);
        if (length == 0)
        {
            return 0;
        }

        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (int i = 0; i < length; i++)
        {
            dot += left[i] * right[i];
            leftMagnitude += left[i] * left[i];
            rightMagnitude += right[i] * right[i];
        }

        if (leftMagnitude <= 0 || rightMagnitude <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }
}
