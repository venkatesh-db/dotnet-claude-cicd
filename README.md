# dotnet-claude-cicd

.NET 8 solution demonstrating Claude wired into the GitHub development lifecycle:
PR review, CI-failure triage, and patch proposals via GitHub Actions.

## Solution layout

- `src/GithubIntegration.Api` — ASP.NET Core Web API (future webhook host)
- `src/GithubIntegration.Core` — domain models and triage rules (no GitHub SDK dependency)
- `src/GithubIntegration.GitHubClient` — Octokit.net wrapper implementing `IGitHubOperations`
- `tests/GithubIntegration.Tests` — xUnit tests for triage logic
- `tools/PatchProposalRunner` — console app that calls `IGitHubOperations.CreateDraftPullRequestAsync`; invoked by the patch-proposal workflow
- `.github/workflows/build-and-test.yml` — CI: restore, build, test on every PR
- `.github/workflows/claude-pr-review.yml` — calls Claude to review PR diffs, posts as a PR comment
- `.github/workflows/claude-ci-triage.yml` — on CI failure, fetches failed job logs, asks Claude to summarize root cause, opens an issue labeled `triage:ci-failure`
- `.github/workflows/claude-patch-proposal.yml` — when an issue is labeled `triage:ci-failure`, asks Claude for a root-cause note + proposed fix, then runs `PatchProposalRunner` to open a **draft PR** with that content (never auto-merged)

## Required repo setup

1. Add a repository secret `ANTHROPIC_API_KEY` (Settings → Secrets and variables → Actions).
   `GITHUB_TOKEN` is provided automatically by Actions.
2. Ensure Actions has permission to create issues/PR comments:
   Settings → Actions → General → Workflow permissions → "Read and write permissions".

## Local development

Requires .NET 8 SDK.

```bash
dotnet restore
dotnet build
dotnet test
```

## Safety rails

- Claude never pushes to `main` directly; patch proposals land as draft PRs requiring human review.
- No auto-merge is implemented anywhere in this repo.
