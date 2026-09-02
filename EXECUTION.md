# How to run this project

Repo: https://github.com/venkatesh-db/dotnet-claude-cicd

## Prerequisites

- .NET 8 SDK (`brew install --cask dotnet-sdk` — see SETUP.md, this was not completed
  automatically and needs your sudo password)
- `gh` CLI, authenticated (`gh auth status` should show `repo` and `workflow` scopes)
- Repo secret `ANTHROPIC_API_KEY` already set (see SETUP.md)

## 1. Local development

```bash
git clone https://github.com/venkatesh-db/dotnet-claude-cicd.git
cd dotnet-claude-cicd
dotnet restore
dotnet build
dotnet test
```

Expected: 7 tests pass in `GithubIntegration.Tests` (triage-labeling logic).

To run the API project locally:

```bash
dotnet run --project src/GithubIntegration.Api
```

## 2. How the automation actually runs (nothing to "start" — it's event-driven)

You don't run these yourself day to day — GitHub Actions runs them automatically:

### a) PR review
Open (or push to) any pull request against `main` → `.github/workflows/claude-pr-review.yml`
fires automatically → diffs the PR → sends it to the Claude API → posts the review as a PR
comment.

To test manually:
```bash
git checkout -b some-branch
# make a change
git commit -am "test change"
git push -u origin some-branch
gh pr create --repo venkatesh-db/dotnet-claude-cicd --base main --head some-branch \
  --title "test" --body "test"
```

### b) CI-failure triage + patch proposal (chained)
Any push/PR that makes `.github/workflows/build-and-test.yml` fail automatically fires
`.github/workflows/claude-ci-triage.yml`, which:
1. **`triage` job** — fetches the failed job's logs, asks Claude to summarize the root
   cause, opens a GitHub issue labeled `triage:ci-failure`.
2. **`propose-patch` job** (runs right after, same workflow) — asks Claude for a root-cause
   note + proposed fix, then runs `tools/PatchProposalRunner` (a small .NET console app using
   the `IGitHubOperations` Octokit wrapper) to create a new branch, commit a file, and open
   a **draft PR** — never auto-merged.

To test manually: push a commit that breaks the build (e.g. a compile error) to any branch,
open a PR against `main`, and watch:
```bash
gh run list --repo venkatesh-db/dotnet-claude-cicd --limit 10
```

### c) Manual patch-proposal trigger
There is also a standalone `.github/workflows/claude-patch-proposal.yml` that fires when a
human manually adds the `triage:ci-failure` label to any issue (this path works because
human-applied labels DO trigger workflows, unlike the ones the bot itself creates — see
CONVERSATION_LOG.md for why that distinction matters).

## 3. Watching a run

```bash
gh run list --repo venkatesh-db/dotnet-claude-cicd --limit 10
gh run view <run-id> --repo venkatesh-db/dotnet-claude-cicd --log-failed
```

## 4. Verified proof this actually works (as of 2026-09-02)

- PR review comment posted on [PR #2](https://github.com/venkatesh-db/dotnet-claude-cicd/pull/2)
- Triage issue opened: [#4](https://github.com/venkatesh-db/dotnet-claude-cicd/issues/4), [#9](https://github.com/venkatesh-db/dotnet-claude-cicd/issues/9)
- Draft patch-proposal PR opened automatically: [#10](https://github.com/venkatesh-db/dotnet-claude-cicd/pull/10)

## 5. Safety rails (by design)

- Claude never pushes to `main` directly.
- Patch proposals always land as **draft** PRs — a human must review and mark ready/merge.
- No workflow in this repo auto-merges anything.
