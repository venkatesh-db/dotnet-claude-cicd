using GithubIntegration.GitHubClient;

string RequireEnv(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
    }

    return value;
}

var token = RequireEnv("GITHUB_TOKEN");
var owner = RequireEnv("REPO_OWNER");
var repo = RequireEnv("REPO_NAME");
var baseBranch = RequireEnv("BASE_BRANCH");
var newBranch = RequireEnv("NEW_BRANCH");
var title = RequireEnv("PR_TITLE");
var bodyPath = RequireEnv("PR_BODY_FILE");
var patchFilePath = RequireEnv("PATCH_FILE_PATH");
var patchContentPath = RequireEnv("PATCH_CONTENT_FILE");

var body = await File.ReadAllTextAsync(bodyPath);
var patchContent = await File.ReadAllTextAsync(patchContentPath);

var client = GitHubClientFactory.CreateFromToken(token);
var operations = new GitHubOperations(client);

var fileChanges = new Dictionary<string, string> { [patchFilePath] = patchContent };

var prUrl = await operations.CreateDraftPullRequestAsync(
    owner,
    repo,
    baseBranch,
    newBranch,
    title,
    body,
    fileChanges);

Console.WriteLine($"Draft PR created: {prUrl}");
