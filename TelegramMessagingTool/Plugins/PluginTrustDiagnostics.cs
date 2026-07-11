using System.Security.Cryptography;

namespace TelegramMessagingTool.Plugins;

public static class PluginTrustDiagnostics
{
    public static string ComputeSha256(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsAllowed(string sha256, IReadOnlySet<string> allowedHashes)
    {
        return allowedHashes.Contains(sha256);
    }

    public static string RenderHashPrefix(string sha256)
    {
        return string.IsNullOrWhiteSpace(sha256)
            ? "missing"
            : sha256[..Math.Min(12, sha256.Length)];
    }
}
