using GithubIntegration.Core.Abstractions;
using Octokit;

namespace GithubIntegration.GitHubClient;

public class GitHubOperations : IGitHubOperations
{
    private readonly IGitHubClient _client;

    public GitHubOperations(IGitHubClient client)
    {
        _client = client;
    }

    public async Task<int> CreateIssueAsync(
        string owner,
        string repo,
        string title,
        string body,
        IReadOnlyList<string>? labels = null,
        CancellationToken ct = default)
    {
        var newIssue = new NewIssue(title) { Body = body };
        if (labels is not null)
        {
            foreach (var label in labels)
            {
                newIssue.Labels.Add(label);
            }
        }

        var issue = await _client.Issue.Create(owner, repo, newIssue);
        return issue.Number;
    }

    public async Task CommentOnPullRequestAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        string body,
        CancellationToken ct = default)
    {
        await _client.Issue.Comment.Create(owner, repo, pullRequestNumber, body);
    }

    public async Task<string> GetFailedJobLogsAsync(
        string owner,
        string repo,
        long workflowRunId,
        CancellationToken ct = default)
    {
        var jobs = await _client.Actions.Workflows.Jobs.List(owner, repo, workflowRunId);
        var failedJobNames = jobs.Jobs
            .Where(j => j.Conclusion == WorkflowJobConclusion.Failure)
            .Select(j => j.Name);

        return string.Join(Environment.NewLine, failedJobNames);
    }

    public async Task<string> CreateDraftPullRequestAsync(
        string owner,
        string repo,
        string baseBranch,
        string newBranch,
        string title,
        string body,
        IReadOnlyDictionary<string, string> fileChanges,
        CancellationToken ct = default)
    {
        var baseReference = await _client.Git.Reference.Get(owner, repo, $"heads/{baseBranch}");

        await _client.Git.Reference.Create(
            owner,
            repo,
            new NewReference($"refs/heads/{newBranch}", baseReference.Object.Sha));

        foreach (var (path, content) in fileChanges)
        {
            var existing = await TryGetFileShaAsync(owner, repo, path, newBranch);
            if (existing is null)
            {
                await _client.Repository.Content.CreateFile(
                    owner,
                    repo,
                    path,
                    new CreateFileRequest($"chore: propose patch for {path}", content, newBranch));
            }
            else
            {
                await _client.Repository.Content.UpdateFile(
                    owner,
                    repo,
                    path,
                    new UpdateFileRequest($"chore: propose patch for {path}", content, existing, newBranch));
            }
        }

        var pullRequest = await _client.PullRequest.Create(
            owner,
            repo,
            new NewPullRequest(title, newBranch, baseBranch) { Body = body, Draft = true });

        return pullRequest.HtmlUrl;
    }

    private async Task<string?> TryGetFileShaAsync(string owner, string repo, string path, string branch)
    {
        try
        {
            var contents = await _client.Repository.Content.GetAllContentsByRef(owner, repo, path, branch);
            return contents.FirstOrDefault()?.Sha;
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}
