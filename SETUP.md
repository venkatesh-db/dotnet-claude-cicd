# Setup Log — what was installed and configured

This documents every tool install, credential, and GitHub repo setting used to build
and run this project, in the order they were done.

## 1. Local tools installed

| Tool | Command | Notes |
|---|---|---|
| GitHub CLI (`gh`) | `brew install gh` | Installed 2.99.0 |
| .NET SDK | already present: 6.0.201, 6.0.400 | **.NET 8 SDK was NOT successfully installed** — `brew install --cask dotnet-sdk` failed because it needs `sudo` and no terminal was available for the password prompt. Local `dotnet build` currently fails since all projects target `net8.0`. **You must run `brew install --cask dotnet-sdk` yourself** to build locally. |

## 2. GitHub authentication

```bash
gh auth login --hostname github.com --git-protocol https --web --scopes "repo,workflow"
```

Done twice: once without the `workflow` scope (failed to push `.github/workflows/*`), then
re-run with `repo,workflow` scopes. Device-code flow — you approved it at
`https://github.com/login/device` in your browser both times.

## 3. Repository

- Created by you: `https://github.com/venkatesh-db/dotnet-claude-cicd` (public)
- Cloned locally into this folder (`Day4-cicd`, originally created as `Day4`)

## 4. Secrets

```bash
gh secret set ANTHROPIC_API_KEY --repo venkatesh-db/dotnet-claude-cicd
```

Set by you, directly in your terminal, piping the key in via `echo "<key>" | gh secret set ...`.
**Two keys were accidentally pasted into the chat during this process and are considered
compromised — both were told to be revoked. The key currently active in the repo secret is
the second one you set; rotate it again if you have not already, since it was also visible
in chat.**

## 5. Repository settings changed via `gh api`

Two repo-level Actions settings had to be changed — these are NOT in any workflow YAML,
they're account/repo settings that cap what any workflow's `GITHUB_TOKEN` can do:

```bash
# Allow workflows to write (create issues, push commits), not just read
gh api -X PUT repos/venkatesh-db/dotnet-claude-cicd/actions/permissions/workflow \
  -f default_workflow_permissions=write \
  -F can_approve_pull_request_reviews=true
```

Before this: `default_workflow_permissions` was `read` and Actions could not create pull
requests at all — this caused two of the failed verification runs (see CONVERSATION_LOG.md).

## 6. GitHub labels created

```bash
gh label create "triage:ci-failure" --repo venkatesh-db/dotnet-claude-cicd --color "d93f0b"
gh label create "triage:needs-manual-review" --repo venkatesh-db/dotnet-claude-cicd --color "fbca04"
gh label create "bug:null-reference" --repo venkatesh-db/dotnet-claude-cicd --color "b60205"
gh label create "bug:timeout" --repo venkatesh-db/dotnet-claude-cicd --color "b60205"
gh label create "build:dependency" --repo venkatesh-db/dotnet-claude-cicd --color "5319e7"
gh label create "build:compile-error" --repo venkatesh-db/dotnet-claude-cicd --color "5319e7"
gh label create "test:assertion-failure" --repo venkatesh-db/dotnet-claude-cicd --color "0e8a16"
gh label create "infra:resource-limit" --repo venkatesh-db/dotnet-claude-cicd --color "1d76db"
gh label create "auth:credential-issue" --repo venkatesh-db/dotnet-claude-cicd --color "1d76db"
```

These match the labels `CiFailureTriageRules.LabelsFor()` computes in code, and the
`triage:ci-failure` label the CI-triage workflow applies when opening an issue.

## 7. NuGet packages added

| Package | Project | Purpose |
|---|---|---|
| `Octokit` 14.0.0 | `GithubIntegration.GitHubClient` | GitHub REST API client |
| `Serilog.AspNetCore` 10.0.0 | `GithubIntegration.Api` | Structured logging (API project, not yet used at runtime) |

## 8. What's still outstanding on your end

1. **Install .NET 8 SDK locally**: `brew install --cask dotnet-sdk` (needs your password — run it yourself in Terminal)
2. **Rotate the Anthropic API key again** if you haven't — it was pasted into this chat twice
3. Nothing else — the repo, secrets, permissions, and labels are all live and verified working
