namespace GithubIntegration.Core.Abstractions;

public interface IGitHubOperations
{
    Task<int> CreateIssueAsync(string owner, string repo, string title, string body, IReadOnlyList<string>? labels = null, CancellationToken ct = default);

    Task CommentOnPullRequestAsync(string owner, string repo, int pullRequestNumber, string body, CancellationToken ct = default);

    Task<string> GetFailedJobLogsAsync(string owner, string repo, long workflowRunId, CancellationToken ct = default);

    Task<string> CreateDraftPullRequestAsync(
        string owner,
        string repo,
        string baseBranch,
        string newBranch,
        string title,
        string body,
        IReadOnlyDictionary<string, string> fileChanges,
        CancellationToken ct = default);
}
