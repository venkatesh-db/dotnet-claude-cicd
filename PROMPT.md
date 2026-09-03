# Master prompt — regenerate this project from scratch

Paste everything below to Claude (or any coding agent with shell + `gh` CLI access) to
rebuild this exact project: a .NET solution with Claude wired into GitHub Actions for
PR review, CI-failure triage, and patch proposals.

---

## PROMPT STARTS HERE

You are building a production-level .NET 8 project that wires Claude into the GitHub
development lifecycle: PR review, CI-failure triage, and automated patch proposals — all
running as real GitHub Actions workflows against a real GitHub repository, not a mockup.

### 0. Environment setup (do this first, verify each step)

1. Install the GitHub CLI: `brew install gh`
2. Authenticate with BOTH `repo` and `workflow` scopes (the `workflow` scope is required to
   push files under `.github/workflows/` — a plain `repo` scope will be rejected by GitHub
   on push):
   ```bash
   gh auth login --hostname github.com --git-protocol https --web --scopes "repo,workflow"
   ```
3. Confirm the .NET 8 SDK is installed: `dotnet --list-sdks`. If only .NET 6/7 SDKs are
   present, either install .NET 8 (`brew install --cask dotnet-sdk` — this needs sudo, tell
   the user to run it themselves if you can't provide a password) or proceed and retarget
   every `.csproj` to `net8.0` before the first CI run (see step 6 — do NOT leave anything on
   `net6.0`, the GitHub Actions runner will not have that runtime installed).
4. Ask the user which GitHub account/org to build under, and whether to create a new repo
   or use an existing one. Creating a new public repo is a side-effecting action — either
   ask the user to create it themselves and give you the URL, or get explicit confirmation
   before running `gh repo create`.
5. Clone the repo locally and work inside it for everything below.

### 1. Solution structure

Create a solution with these projects:

```bash
dotnet new sln -n <SolutionName>
dotnet new webapi -n GithubIntegration.Api -o src/GithubIntegration.Api --no-https
dotnet new classlib -n GithubIntegration.Core -o src/GithubIntegration.Core
dotnet new classlib -n GithubIntegration.GitHubClient -o src/GithubIntegration.GitHubClient
dotnet new xunit -n GithubIntegration.Tests -o tests/GithubIntegration.Tests
dotnet new console -n PatchProposalRunner -o tools/PatchProposalRunner

dotnet sln add src/GithubIntegration.Api src/GithubIntegration.Core \
  src/GithubIntegration.GitHubClient tests/GithubIntegration.Tests \
  tools/PatchProposalRunner

dotnet add src/GithubIntegration.Api reference src/GithubIntegration.Core src/GithubIntegration.GitHubClient
dotnet add src/GithubIntegration.GitHubClient reference src/GithubIntegration.Core
dotnet add tests/GithubIntegration.Tests reference src/GithubIntegration.Core src/GithubIntegration.GitHubClient
dotnet add tools/PatchProposalRunner reference src/GithubIntegration.Core src/GithubIntegration.GitHubClient

dotnet add src/GithubIntegration.GitHubClient package Octokit
dotnet add src/GithubIntegration.Api package Serilog.AspNetCore
```

**Every `.csproj` must target `net8.0`.** Do not scaffold against whatever SDK happens to be
locally installed if it's older — GitHub's `actions/setup-dotnet@v4` with `dotnet-version:
"8.0.x"` installs only 8.x+ runtimes, and a `net6.0` binary will crash at launch on the
runner with "You must install or update .NET to run this application."

### 2. Domain logic (`GithubIntegration.Core`)

- `Models/PullRequestReview.cs` — a `record` with repo owner/name, PR number, a summary
  string, and a list of `PullRequestReviewComment` (file path, line, body).
- `Models/CiFailureTriage.cs` — a `record` with repo owner/name, workflow run id, workflow
  name, a root-cause summary string, and a list of labels.
- `Triage/CiFailureTriageRules.cs` — a static class, `LabelsFor(string failureLog)` that
  pattern-matches known failure signatures (e.g. `NullReferenceException` →
  `bug:null-reference`, `error CS` → `build:compile-error`, `Timeout` → `bug:timeout`,
  `OutOfMemory` → `infra:resource-limit`, `Unauthorized` → `auth:credential-issue`) to a list
  of label strings, falling back to `triage:needs-manual-review` when nothing matches or the
  log is empty. Also expose `BuildTriage(...)` that returns a populated `CiFailureTriage`
  record using `LabelsFor`.
- Write full xUnit coverage for `CiFailureTriageRules` in `tests/GithubIntegration.Tests`:
  one test per signature, one for the unrecognized-log fallback, one for the empty-log
  fallback, one for multiple simultaneous signatures, one for `BuildTriage`.

### 3. GitHub API layer (`GithubIntegration.GitHubClient`)

- `GitHubClientFactory.CreateFromToken(string token)` — returns an `Octokit.GitHubClient`
  authenticated with a personal/Actions token via `Credentials`.
- `Abstractions/IGitHubOperations.cs` (in Core) with:
  - `CreateIssueAsync(owner, repo, title, body, labels?, ct)` → returns issue number
  - `CommentOnPullRequestAsync(owner, repo, prNumber, body, ct)`
  - `GetFailedJobLogsAsync(owner, repo, workflowRunId, ct)` → returns failed job names
    joined by newline (via `Actions.Workflows.Jobs.List` filtered to `Conclusion == Failure`)
  - `CreateDraftPullRequestAsync(owner, repo, baseBranch, newBranch, title, body,
    fileChanges: IReadOnlyDictionary<string,string>, ct)` — creates a new branch ref from the
    base branch's current SHA, creates or updates each file in `fileChanges` on that branch
    (check for existing SHA first via `GetAllContentsByRef`, catching `NotFoundException` to
    decide create vs update), then opens a PR with `Draft = true`. Returns the PR's HTML URL.
- `GitHubOperations : IGitHubOperations` implementing all of the above via the injected
  `IGitHubClient`.

### 4. Patch-proposal runner (`tools/PatchProposalRunner`, console app)

A minimal `Program.cs` that reads required env vars (`GITHUB_TOKEN`, `REPO_OWNER`,
`REPO_NAME`, `BASE_BRANCH`, `NEW_BRANCH`, `PR_TITLE`, `PR_BODY_FILE`, `PATCH_FILE_PATH`,
`PATCH_CONTENT_FILE`), throws clearly if any is missing, reads the body/patch content from
the given file paths, builds a `GitHubOperations` via the factory, and calls
`CreateDraftPullRequestAsync` with a single-file change. Print the resulting PR URL.

### 5. GitHub Actions workflows

Create four workflow files under `.github/workflows/`:

**`build-and-test.yml`** — on `pull_request`/`push` to `main`: `actions/checkout@v4`,
`actions/setup-dotnet@v4` (`dotnet-version: "8.0.x"`), `dotnet restore/build/test`, upload
`.trx` results as an artifact.

**`claude-pr-review.yml`** — on `pull_request` (`opened, synchronize, reopened`), permissions
`contents: read`, `pull-requests: write`:
1. Checkout with `fetch-depth: 0`, `git diff` base SHA to head SHA into a file, trim to 60KB.
2. **Build the Anthropic API request payload with a real JSON serializer, never a bash
   heredoc string concatenation.** Use an inline `python3 - <<'PYEOF' ... PYEOF` block that
   reads the diff file, builds a Python dict `{"model": "claude-sonnet-4-5", "max_tokens":
   1024, "messages": [{"role": "user", "content": <prompt+diff>}]}`, and writes it with
   `json.dump()` to `payload.json`. (Embedding a `json.dumps(...)` call's *output* directly
   inside an already-quoted bash heredoc JSON string produces malformed JSON with doubled
   quotes — this is a real bug to avoid, not a hypothetical one.)
3. `curl -sS https://api.anthropic.com/v1/messages -H "x-api-key: $ANTHROPIC_API_KEY" -H
   "anthropic-version: 2023-06-01" -H "content-type: application/json" -d @payload.json >
   response.json`
4. Parse the response with another inline Python block: if `"content"` is missing from the
   JSON, print the whole error response to stderr and `sys.exit(1)` (surface API errors
   instead of crashing on a bare `KeyError`); otherwise write
   `response["content"][0]["text"]` to `review.md`.
5. `gh pr comment <pr-number> --body-file review.md` using `GITHUB_TOKEN`.

**`claude-ci-triage.yml`** — on `workflow_run` (`workflows: ["Build and Test"]`, types:
`[completed]`), gated on `github.event.workflow_run.conclusion == 'failure'`. Permissions
must be `contents: write`, `pull-requests: write`, `issues: write`, `actions: read` — not
just read, because a second job in this same workflow needs to open a PR (see below). Two
jobs:
- **`triage`** job: fetch failed job logs (`gh run view <run-id> --log-failed`), trim, build
  and send the same kind of JSON payload to Claude asking for a 3-5 sentence root-cause
  summary + fix suggestion, write it to `summary.md`, then `gh issue create --title "CI
  failure: run #<id> on <branch>" --body-file summary.md --label "triage:ci-failure"` —
  capture the created issue's number as a job output (`echo "issue_number=..." >>
  "$GITHUB_OUTPUT"`, parsed from the issue URL `gh issue create` prints).
- **`propose-patch`** job, `needs: triage`: **do NOT make this a separate workflow triggered
  by `issues: labeled`.** GitHub blocks a workflow's own `GITHUB_TOKEN`-created events (like
  the label just added by the `triage` job) from triggering other workflows — this is
  anti-recursion protection, and an `issues.labeled`-triggered workflow will simply never
  fire for automatically-created issues. Chain it as a second job in the *same* workflow
  file instead, reading `needs.triage.outputs.issue_number`. This job: reads the issue's
  title/body via `gh issue view --json title,body --jq ...`, asks Claude for a markdown
  incident note (root cause, evidence, proposed fix) the same way as above, writes it to
  `patch-note.md` plus a short `pr-body.md`, then runs `dotnet run --project
  tools/PatchProposalRunner` with all the env vars it needs (branch name
  `patch-proposal/issue-<n>`, file path `docs/incidents/issue-<n>.md`, etc.) to actually
  create the draft PR.

**`claude-patch-proposal.yml`** (optional, keep for the manual path) — on `issues: labeled`,
gated on `github.event.label.name == 'triage:ci-failure'`. Same steps as the `propose-patch`
job above. This *does* work for the one case the automated path can't cover: a human
manually applying the label to any issue.

### 6. Repository configuration (these are NOT in any YAML — verify them explicitly)

1. Set the repo secret: `gh secret set ANTHROPIC_API_KEY --repo <owner>/<repo>` — **never**
   ask the user to paste the key value into chat; tell them to pipe it directly in their own
   terminal (`echo "<key>" | gh secret set ANTHROPIC_API_KEY --repo <owner>/<repo>`). If a
   key is ever pasted into a chat/session log, treat it as compromised and tell the user to
   revoke and rotate it immediately, even if it still technically works.
2. Check and fix the repo's default Actions token permission — it defaults to **read-only**
   on many accounts, which silently caps every workflow's `GITHUB_TOKEN` regardless of what
   the workflow YAML's own `permissions:` block requests:
   ```bash
   gh api repos/<owner>/<repo>/actions/permissions/workflow
   gh api -X PUT repos/<owner>/<repo>/actions/permissions/workflow \
     -f default_workflow_permissions=write \
     -F can_approve_pull_request_reviews=true
   ```
   The second flag is a *separate* toggle ("Allow GitHub Actions to create pull requests")
   from the first — both are required, and missing either produces a distinct, specific
   error (`Resource not accessible by integration` for the first, `GitHub Actions is not
   permitted to create or approve pull requests` for the second). Don't guess; read the
   actual `gh run view --log-failed` output to tell which one is still missing.
3. Create every label the triage rules and workflows reference before the first real run —
   `gh issue create --label` fails hard if the label doesn't exist yet:
   ```bash
   gh label create "triage:ci-failure" --repo <owner>/<repo> --color "d93f0b"
   gh label create "triage:needs-manual-review" --repo <owner>/<repo> --color "fbca04"
   gh label create "bug:null-reference" --repo <owner>/<repo> --color "b60205"
   gh label create "bug:timeout" --repo <owner>/<repo> --color "b60205"
   gh label create "build:dependency" --repo <owner>/<repo> --color "5319e7"
   gh label create "build:compile-error" --repo <owner>/<repo> --color "5319e7"
   gh label create "test:assertion-failure" --repo <owner>/<repo> --color "0e8a16"
   gh label create "infra:resource-limit" --repo <owner>/<repo> --color "1d76db"
   gh label create "auth:credential-issue" --repo <owner>/<repo> --color "1d76db"
   ```

### 7. Verification — do not claim any workflow works without proof

For each of the three pillars, force a real trigger and read the actual run logs:

1. **PR review**: open a real PR against `main` with a small code change. Watch
   `gh run list --workflow "Claude PR Review"` reach `completed/success`, then
   `gh pr view <n> --comments` and confirm Claude's comment actually posted with real
   content (not just that the job exited 0).
2. **CI-triage + patch-proposal**: push a commit that deliberately breaks the build (a
   compile error is easiest and unambiguous) on a branch, open a PR so `build-and-test.yml`
   runs and fails, then watch the chained `Claude CI Failure Triage` workflow. Confirm with
   `gh issue list --label "triage:ci-failure"` that a real issue appeared with an accurate
   root-cause summary, and `gh pr list --draft` that a real draft PR appeared referencing
   that issue.
3. If any run fails, **read the actual failed step's log** (`gh run view <id>
   --log-failed`) before changing anything — do not guess at fixes. Expect to hit, in
   roughly this order, most of: malformed JSON payloads (fix: build with a real serializer),
   the `issues.labeled` recursion block (fix: chain jobs, don't use a second triggered
   workflow), a `net6.0`/`net8.0` runtime mismatch (fix: retarget every `.csproj`), and the
   two-layer permissions gap in section 6 (fix: both the workflow YAML's `permissions:` block
   AND the repo's default Actions token settings must allow write).
4. Once real, once broken build lands, immediately revert or clean up the intentionally
   broken test file and any demo branches/PRs used purely for verification.

### 8. Documentation to produce alongside the code

- `README.md` — solution layout, required repo setup (secret + permissions), how to build/
  test locally, and the safety rails (never auto-merge, patches always land as draft PRs).
- `SETUP.md` — a literal log of every tool installed, credential configured, and repo
  setting changed, in the order it was done, including what's still pending on the user
  (e.g. a missing local SDK install that needs their password).
- `EXECUTION.md` — practical "how to run and test this" guide: prerequisites, local dev
  commands, how each of the three automations actually triggers, links to the real
  issues/PRs that proved it works.

### 9. Safety rails — do not deviate from these

- Claude-authored changes never push directly to `main` and never auto-merge anywhere in
  the repo — every patch proposal lands as a **draft** PR requiring human review.
- Never enter, echo, or persist an API key, token, or password on the user's behalf — if one
  appears in chat, say so and tell the user to rotate it, don't use it.
- Creating a new public repo, pushing to a shared branch, and changing repo-wide security
  settings (like default Actions permissions) are all side-effecting actions — confirm with
  the user before doing them, don't assume authorization carries over from an earlier
  approval.

## PROMPT ENDS HERE

---

*This prompt was extracted from a real build session — every bug and fix listed in section 7
actually happened and was diagnosed from real GitHub Actions logs, not anticipated in
advance. See `CONVERSATION_LOG.md` in this repo for the full narrative.*
