using Octokit;

namespace GithubIntegration.GitHubClient;

public static class GitHubClientFactory
{
    private const string ProductName = "dotnet-claude-cicd";

    public static IGitHubClient CreateFromToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("A GitHub token is required.", nameof(token));
        }

        return new Octokit.GitHubClient(new ProductHeaderValue(ProductName))
        {
            Credentials = new Credentials(token)
        };
    }
}
