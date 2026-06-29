namespace TelegramMessagingTool.Tools.GitHub;

public static class GitHubRepoPolicy
{
    public static IReadOnlySet<string> ParseAllowedRepos(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeFullName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsAllowed(string owner, string repo, IReadOnlySet<string> allowedRepos)
    {
        string normalized = NormalizeFullName(owner, repo);
        return !string.IsNullOrWhiteSpace(normalized) && allowedRepos.Contains(normalized);
    }

    public static string NormalizeFullName(string owner, string repo)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return string.Empty;
        }

        return NormalizeFullName($"{owner}/{repo}");
    }

    public static string NormalizeFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return string.Empty;
        }

        string[] parts = fullName.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return string.Empty;
        }

        return $"{parts[0]}/{parts[1]}";
    }
}
