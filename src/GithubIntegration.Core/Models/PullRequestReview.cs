namespace GithubIntegration.Core.Models;

public record PullRequestReview(
    string RepoOwner,
    string RepoName,
    int PullRequestNumber,
    string Summary,
    IReadOnlyList<PullRequestReviewComment> Comments);

public record PullRequestReviewComment(string FilePath, int Line, string Body);
