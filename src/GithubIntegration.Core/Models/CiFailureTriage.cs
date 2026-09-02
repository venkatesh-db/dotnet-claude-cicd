namespace GithubIntegration.Core.Models;

public record CiFailureTriage(
    string RepoOwner,
    string RepoName,
    long WorkflowRunId,
    string WorkflowName,
    string RootCauseSummary,
    IReadOnlyList<string> Labels);
