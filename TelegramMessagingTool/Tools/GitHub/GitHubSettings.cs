namespace TelegramMessagingTool.Tools.GitHub;

public sealed record GitHubSettings(
    bool EnableGitHubTools,
    bool EnableGitHubWriteTools,
    string Token,
    string DefaultOwner,
    string DefaultRepo,
    IReadOnlySet<string> AllowedRepos)
{
    public static GitHubSettings Disabled { get; } = new(
        EnableGitHubTools: false,
        EnableGitHubWriteTools: false,
        Token: string.Empty,
        DefaultOwner: string.Empty,
        DefaultRepo: string.Empty,
        AllowedRepos: new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public string DefaultFullName => string.IsNullOrWhiteSpace(DefaultOwner) || string.IsNullOrWhiteSpace(DefaultRepo)
        ? string.Empty
        : $"{DefaultOwner}/{DefaultRepo}";

    public static GitHubSettings LoadFromEnvironment()
    {
        bool enableGitHubTools = BotConfiguration.IsEnabled(Environment.GetEnvironmentVariable("ENABLE_GITHUB_TOOLS"), defaultValue: false);
        bool enableGitHubWriteTools = BotConfiguration.IsEnabled(Environment.GetEnvironmentVariable("ENABLE_GITHUB_WRITE_TOOLS"), defaultValue: false);
        string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")?.Trim() ?? string.Empty;
        string defaultOwner = Environment.GetEnvironmentVariable("GITHUB_DEFAULT_OWNER")?.Trim() ?? string.Empty;
        string defaultRepo = Environment.GetEnvironmentVariable("GITHUB_DEFAULT_REPO")?.Trim() ?? string.Empty;
        IReadOnlySet<string> allowedRepos = GitHubRepoPolicy.ParseAllowedRepos(Environment.GetEnvironmentVariable("GITHUB_ALLOWED_REPOS"));

        if (allowedRepos.Count == 0 && !string.IsNullOrWhiteSpace(defaultOwner) && !string.IsNullOrWhiteSpace(defaultRepo))
        {
            allowedRepos = new HashSet<string>([$"{defaultOwner}/{defaultRepo}"], StringComparer.OrdinalIgnoreCase);
        }

        return new GitHubSettings(enableGitHubTools, enableGitHubWriteTools, token, defaultOwner, defaultRepo, allowedRepos);
    }

    public string RenderSafeSummary()
    {
        string defaultRepoText = string.IsNullOrWhiteSpace(DefaultFullName) ? "not configured" : DefaultFullName;
        string tokenStatus = string.IsNullOrWhiteSpace(Token) ? "not configured" : "configured";
        string allowedRepoText = AllowedRepos.Count == 0 ? "none" : string.Join(", ", AllowedRepos.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        return $"GitHub tools: {(EnableGitHubTools ? "enabled" : "disabled")}; GitHub write tools: {(EnableGitHubWriteTools ? "enabled" : "disabled")}; default repo: {defaultRepoText}; allowed repos: {allowedRepoText}; token: {tokenStatus}";
    }
}
