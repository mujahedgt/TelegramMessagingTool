using TelegramMessagingTool.Services;
using TelegramMessagingTool.Tools.CommandExecution;
using TelegramMessagingTool.Tools.GitHub;

namespace TelegramMessagingTool.Tools;

public static class ToolRegistryFactory
{
    public static ToolRegistry Create(BotSettings settings, HttpClient searchClient, PendingActionService? pendingActionService = null)
    {
        var tools = new List<IAgentTool>
        {
            new DateTimeTool(),
            new CalculatorTool(),
            new BotStatusTool(settings)
        };

        if (settings.EnableOnlineSearch)
        {
            tools.Add(new OnlineSearchTool(searchClient));
        }

        if (settings.GitHub.EnableGitHubTools)
        {
            tools.Add(new GitHubRepoInfoTool(searchClient, settings.GitHub));
            tools.Add(new GitHubListIssuesTool(searchClient, settings.GitHub));
            tools.Add(new GitHubGetIssueTool(searchClient, settings.GitHub));
            tools.Add(new GitHubListPullRequestsTool(searchClient, settings.GitHub));
            tools.Add(new GitHubGetPullRequestStatusTool(searchClient, settings.GitHub));
        }

        if (settings.GitHub.EnableGitHubWriteTools && pendingActionService is not null)
        {
            tools.Add(new GitHubCreateIssueRequestTool(pendingActionService, settings));
        }

        if (settings.EnableSafeCommandTools)
        {
            tools.Add(new GitStatusTool(settings.SafeCommandProjectRoot));
            tools.Add(new GitDiffTool(settings.SafeCommandProjectRoot));
            tools.Add(new GitLogRecentTool(settings.SafeCommandProjectRoot));
            tools.Add(new RunDotnetTestsTool(settings.SafeCommandProjectRoot));

            if (pendingActionService is not null)
            {
                tools.Add(new PublishReleaseRequestTool(pendingActionService, settings, settings.SafeCommandProjectRoot));
                tools.Add(new RestartLatestBotRequestTool(pendingActionService, settings, settings.SafeCommandProjectRoot));
            }
        }

        if (settings.EnableRepoWriteTools && pendingActionService is not null)
        {
            tools.Add(new RepoReplaceTextRequestTool(pendingActionService, settings, settings.SafeCommandProjectRoot));
            tools.Add(new RepoApplyPatchRequestTool(pendingActionService, settings, settings.SafeCommandProjectRoot));
            tools.Add(new RepoCommitChangesRequestTool(pendingActionService, settings, settings.SafeCommandProjectRoot));
            tools.Add(new RepoPushChangesRequestTool(pendingActionService, settings, settings.SafeCommandProjectRoot));
        }

        return new ToolRegistry(tools);
    }
}
