# How to run this project — Windows

Repo: https://github.com/venkatesh-db/dotnet-claude-cicd

Same project, same GitHub Actions workflows — this file only covers the parts that differ
on Windows: installing tools, shell syntax (PowerShell instead of bash), and path handling.
Everything in section 2 onward runs identically once the prerequisites are in place, because
the actual automation lives in GitHub Actions (Linux runners), not on your machine.

## 0. Recommended: use PowerShell, not cmd.exe

Open **PowerShell** (not Command Prompt) for every command below — Windows Terminal or the
PowerShell app both work. `gh` and `dotnet` behave the same in either, but the multi-line
command examples here use PowerShell line-continuation (`` ` `` at end of line), which
cmd.exe doesn't support.

If you have Git Bash installed (comes with Git for Windows), the original
[EXECUTION.md](EXECUTION.md) bash commands work there unmodified — this file is only needed
if you're staying in PowerShell.

## 1. Install prerequisites

```powershell
# .NET 8 SDK
winget install Microsoft.DotNet.SDK.8

# GitHub CLI
winget install --id GitHub.cli

# Git (skip if already installed)
winget install --id Git.Git
```

Close and reopen your terminal after these so `PATH` picks up the new installs. Verify:

```powershell
dotnet --list-sdks
gh --version
git --version
```

You should see an `8.x.x` entry in the SDK list.

## 2. Authenticate `gh`

```powershell
gh auth login --hostname github.com --git-protocol https --web --scopes "repo,workflow"
```

This prints a one-time code and a URL (`https://github.com/login/device`) — open it in your
browser, enter the code, approve. The `workflow` scope is required; without it, pushing
anything under `.github/workflows/` will be rejected later.

Verify:
```powershell
gh auth status
```

## 3. Clone and build locally

```powershell
git clone https://github.com/venkatesh-db/dotnet-claude-cicd.git
cd dotnet-claude-cicd
dotnet restore
dotnet build
dotnet test
```

Expected: 7 tests pass in `GithubIntegration.Tests`.

Run the API project locally:
```powershell
dotnet run --project src\GithubIntegration.Api
```
(Note the backslash in the path — Windows accepts either `\` or `/` here, but `\` is native.)

## 4. Repo secret (if not already set)

```powershell
"YOUR_ANTHROPIC_KEY_HERE" | gh secret set ANTHROPIC_API_KEY --repo venkatesh-db/dotnet-claude-cicd
```

Run this yourself, directly in your own PowerShell window — never paste the key into a chat
with any assistant, including this one. If a key is ever pasted anywhere outside your own
terminal, treat it as compromised and rotate it at console.anthropic.com immediately.

Verify (won't show the value, just confirms it's set):
```powershell
gh secret list --repo venkatesh-db/dotnet-claude-cicd
```

## 5. How the automation runs (identical on every OS — it's server-side)

Nothing below runs on your Windows machine — GitHub's own Linux runners execute all three
workflows. Your machine is only where you push code from.

### a) PR review
Push a branch and open a PR against `main`:
```powershell
git checkout -b some-branch
# ... make a change ...
git commit -am "test change"
git push -u origin some-branch
gh pr create --repo venkatesh-db/dotnet-claude-cicd --base main --head some-branch `
  --title "test" --body "test"
```
`claude-pr-review.yml` fires automatically, diffs the PR, sends it to Claude, and posts the
review as a PR comment.

### b) CI-failure triage + patch proposal (chained)
Push a commit that breaks the build (e.g. a real compile error) and open a PR so
`build-and-test.yml` fails. `claude-ci-triage.yml` then:
1. Fetches the failed job's logs, asks Claude to summarize the root cause, opens a GitHub
   issue labeled `triage:ci-failure`.
2. Immediately runs a second job in the same workflow that asks Claude for a fix and runs
   `tools/PatchProposalRunner` to open a **draft PR** — never auto-merged.

### c) Manual patch-proposal trigger
Manually adding the `triage:ci-failure` label to any issue in the GitHub UI (or via
`gh issue edit <n> --add-label "triage:ci-failure"`) fires the standalone
`claude-patch-proposal.yml` workflow — this path works because a human-applied label *does*
trigger workflows, unlike one applied by `GITHUB_TOKEN` itself (see CONVERSATION_LOG.md).

## 6. Watching a run

```powershell
gh run list --repo venkatesh-db/dotnet-claude-cicd --limit 10
gh run view <run-id> --repo venkatesh-db/dotnet-claude-cicd --log-failed
```

## 7. Verified proof this actually works (as of 2026-09-02)

- PR review comment posted on [PR #2](https://github.com/venkatesh-db/dotnet-claude-cicd/pull/2)
- Triage issues opened: [#4](https://github.com/venkatesh-db/dotnet-claude-cicd/issues/4), [#9](https://github.com/venkatesh-db/dotnet-claude-cicd/issues/9)
- Draft patch-proposal PR opened automatically: [#10](https://github.com/venkatesh-db/dotnet-claude-cicd/pull/10)

These were all verified from a Mac in the original build session, but the workflows run on
GitHub's own Linux runners regardless of what OS you push from, so nothing about the result
differs on Windows.

## 8. Common Windows-specific snags

| Symptom | Fix |
|---|---|
| `gh` / `dotnet` "not recognized" after install | Close and reopen the terminal — `winget` updates `PATH` but open shells don't see it |
| `git commit` opens a strange editor you can't exit | Set a sane default: `git config --global core.editor "notepad"` |
| Line-ending warnings (`LF will be replaced by CRLF`) | Harmless for this repo; set `git config --global core.autocrlf true` if it bothers you |
| PowerShell rejects a multi-line command copied from bash docs | Bash uses `\` for line continuation, PowerShell uses `` ` `` — either retype on one line or swap the character |
| `dotnet test` can't find the SDK | Confirm `dotnet --list-sdks` shows 8.x; if not, the `winget install Microsoft.DotNet.SDK.8` step didn't complete — re-run it |

## 9. Safety rails (by design, same on every OS)

- Claude never pushes to `main` directly.
- Patch proposals always land as **draft** PRs — a human must review and mark ready/merge.
- No workflow in this repo auto-merges anything.
