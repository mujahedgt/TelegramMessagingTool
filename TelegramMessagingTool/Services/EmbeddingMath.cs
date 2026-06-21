using System.Globalization;
using System.Text.Json;

namespace TelegramMessagingTool.Services;

public interface ITextEmbeddingService
{
    Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken cancellationToken);
}

public static class EmbeddingMath
{
    public static string Serialize(IEnumerable<double> values)
    {
        return JsonSerializer.Serialize(values.Select(x => (float)x).ToArray());
    }

    public static string Serialize(IEnumerable<float> values)
    {
        return JsonSerializer.Serialize(values.ToArray());
    }

    public static IReadOnlyList<float> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            float[]? values = JsonSerializer.Deserialize<float[]>(json);
            return values ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count == 0 || right.Count == 0 || left.Count != right.Count)
        {
            return 0;
        }

        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (int i = 0; i < left.Count; i++)
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

    public static string Describe(IReadOnlyList<float> vector)
    {
        return vector.Count == 0
            ? "empty vector"
            : $"{vector.Count} dimensions; first={vector[0].ToString("0.###", CultureInfo.InvariantCulture)}";
    }
}
