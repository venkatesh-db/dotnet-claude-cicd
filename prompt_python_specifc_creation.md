# Master prompt — regenerate this project in Python

Paste everything below to Claude (or any coding agent with shell + `gh` CLI access) to
build the Python equivalent of this project: a Python service with Claude wired into
GitHub Actions for PR review, CI-failure triage, and patch proposals. This is a straight
port of `PROMPT.md` (the .NET version already built in this repo) — same mechanism, same
known bugs to avoid, different language.

---

## PROMPT STARTS HERE

You are building a production-level Python project that wires Claude into the GitHub
development lifecycle: PR review, CI-failure triage, and automated patch proposals — all
running as real GitHub Actions workflows against a real GitHub repository, not a mockup.

### 0. Environment setup (do this first, verify each step)

1. Install the GitHub CLI: `brew install gh` (or the Linux/Windows equivalent).
2. Authenticate with BOTH `repo` and `workflow` scopes — the `workflow` scope is required
   to push files under `.github/workflows/`; plain `repo` scope gets rejected on push:
   ```bash
   gh auth login --hostname github.com --git-protocol https --web --scopes "repo,workflow"
   ```
3. Confirm Python 3.12+ is available: `python3 --version`. If not, install it with your
   platform's package manager — do not silently target an older interpreter than what the
   CI runner will use (`actions/setup-python@v5` should pin the same version, see step 6).
4. Ask the user which GitHub account/org to build under, and whether to create a new repo
   or use an existing one. Creating a new public repo is a side-effecting action — either
   ask the user to create it themselves and give you the URL, or get explicit confirmation
   before running `gh repo create`.
5. Clone the repo locally and work inside it. Create and activate a virtual environment
   before installing anything:
   ```bash
   python3 -m venv .venv
   source .venv/bin/activate   # .venv\Scripts\activate on Windows
   ```

### 1. Project structure

```
src/
  github_integration/
    __init__.py
    models.py            # dataclasses: PullRequestReview, CiFailureTriage
    triage_rules.py       # pattern -> label mapping
    github_operations.py  # PyGithub wrapper implementing the GitHubOperations protocol
    github_client.py      # client factory (token auth)
  api/
    __init__.py
    main.py                # FastAPI app (scaffold — not wired into workflows yet, mirrors
                            # the .NET version's unused Api project)
tools/
  patch_proposal_runner.py  # CLI entry point invoked by the CI-triage workflow
tests/
  test_triage_rules.py
requirements.txt
pyproject.toml
.github/workflows/
  build-and-test.yml
  claude-pr-review.yml
  claude-ci-triage.yml
  claude-patch-proposal.yml
```

Dependencies (`requirements.txt` or `pyproject.toml`):
```
PyGithub>=2.3
fastapi>=0.111
uvicorn>=0.30
pytest>=8.2
```

### 2. Domain logic (`src/github_integration/models.py`, `triage_rules.py`)

Use `@dataclass(frozen=True)` for immutable models — the direct equivalent of the .NET
`record` types:

```python
from dataclasses import dataclass
from typing import Sequence

@dataclass(frozen=True)
class PullRequestReviewComment:
    file_path: str
    line: int
    body: str

@dataclass(frozen=True)
class PullRequestReview:
    repo_owner: str
    repo_name: str
    pull_request_number: int
    summary: str
    comments: Sequence[PullRequestReviewComment]

@dataclass(frozen=True)
class CiFailureTriage:
    repo_owner: str
    repo_name: str
    workflow_run_id: int
    workflow_name: str
    root_cause_summary: str
    labels: Sequence[str]
```

`triage_rules.py` — a pure function, no I/O, easy to unit test:

```python
SIGNATURE_LABELS: list[tuple[str, str]] = [
    ("NullReferenceException", "bug:null-reference"),
    ("Timeout", "bug:timeout"),
    ("pip install", "build:dependency"),
    ("SyntaxError", "build:compile-error"),
    ("AssertionError", "test:assertion-failure"),
    ("MemoryError", "infra:resource-limit"),
    ("Unauthorized", "auth:credential-issue"),
]

def labels_for(failure_log: str) -> list[str]:
    if not failure_log or not failure_log.strip():
        return ["triage:needs-manual-review"]
    matched = [label for pattern, label in SIGNATURE_LABELS
               if pattern.lower() in failure_log.lower()]
    return matched or ["triage:needs-manual-review"]

def build_triage(owner: str, repo: str, run_id: int, workflow_name: str,
                  failure_log: str, root_cause_summary: str) -> CiFailureTriage:
    return CiFailureTriage(owner, repo, run_id, workflow_name,
                            root_cause_summary, labels_for(failure_log))
```

Write full pytest coverage in `tests/test_triage_rules.py`: one test per signature, one for
the unrecognized-log fallback, one for the empty-log fallback, one for multiple
simultaneous signatures, one for `build_triage`. Match the .NET version's 6 test cases.

### 3. GitHub API layer (`github_operations.py`, `github_client.py`)

Use **PyGithub**, not raw `requests` — it's the Python equivalent of Octokit.net and
handles pagination, rate limits, and auth headers correctly.

```python
from github import Github, Auth

def create_client_from_token(token: str) -> Github:
    if not token:
        raise ValueError("A GitHub token is required.")
    return Github(auth=Auth.Token(token))
```

`GitHubOperations` needs these methods (mirror the .NET `IGitHubOperations` interface
exactly — same four operations, same responsibilities):
- `create_issue(owner, repo, title, body, labels=None) -> int` — returns issue number
- `comment_on_pull_request(owner, repo, pr_number, body) -> None`
- `get_failed_job_logs(owner, repo, workflow_run_id) -> str` — join failed job names
- `create_draft_pull_request(owner, repo, base_branch, new_branch, title, body,
  file_changes: dict[str, str]) -> str` — create a branch ref from the base branch's SHA,
  create-or-update each file in `file_changes` (try `repo.get_contents(path, ref=branch)`
  first to get the existing SHA for an update, catch `github.GithubException` for
  not-found to decide create vs update), then open the PR with `draft=True`. Return the
  PR's HTML URL.

### 4. Patch-proposal runner (`tools/patch_proposal_runner.py`)

A CLI script reading required env vars (`GITHUB_TOKEN`, `REPO_OWNER`, `REPO_NAME`,
`BASE_BRANCH`, `NEW_BRANCH`, `PR_TITLE`, `PR_BODY_FILE`, `PATCH_FILE_PATH`,
`PATCH_CONTENT_FILE`), raising a clear `EnvironmentError` if any is missing, reading the
body/patch content from the given file paths, and calling
`GitHubOperations.create_draft_pull_request` with a single-file change:

```python
import os
import sys

def require_env(name: str) -> str:
    value = os.environ.get(name)
    if not value:
        raise EnvironmentError(f"Required environment variable '{name}' is not set.")
    return value

def main() -> None:
    token = require_env("GITHUB_TOKEN")
    owner = require_env("REPO_OWNER")
    repo = require_env("REPO_NAME")
    base_branch = require_env("BASE_BRANCH")
    new_branch = require_env("NEW_BRANCH")
    title = require_env("PR_TITLE")
    body = open(require_env("PR_BODY_FILE")).read()
    patch_path = require_env("PATCH_FILE_PATH")
    patch_content = open(require_env("PATCH_CONTENT_FILE")).read()

    client = create_client_from_token(token)
    ops = GitHubOperations(client)
    pr_url = ops.create_draft_pull_request(
        owner, repo, base_branch, new_branch, title, body, {patch_path: patch_content}
    )
    print(f"Draft PR created: {pr_url}")

if __name__ == "__main__":
    main()
```

### 5. GitHub Actions workflows

Same four workflows as the .NET version, same triggers, same job structure — only the
runtime setup step and the invocation command change.

**`build-and-test.yml`** — on `pull_request`/`push` to `main`: `actions/checkout@v4`,
`actions/setup-python@v5` (`python-version: "3.12"`), `pip install -r requirements.txt`,
`pytest`.

**`claude-pr-review.yml`**, **`claude-ci-triage.yml`**, **`claude-patch-proposal.yml`** —
identical structure and identical **known-bug avoidance** to the .NET version (see section
7 below — these bugs are GitHub/Anthropic-API-level, not language-level, so they apply
here just as much). The only Python-specific change: the final "run patch proposal" step
becomes:
```yaml
      - name: Setup Python
        uses: actions/setup-python@v5
        with:
          python-version: "3.12"
      - name: Install dependencies
        run: pip install -r requirements.txt
      - name: Run patch proposal (creates draft PR)
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          REPO_OWNER: ${{ github.repository_owner }}
          REPO_NAME: ${{ github.event.repository.name }}
          BASE_BRANCH: main
          NEW_BRANCH: patch-proposal/issue-${{ needs.triage.outputs.issue_number }}
          PR_TITLE: "fix: proposed patch for #${{ needs.triage.outputs.issue_number }}"
          PR_BODY_FILE: pr-body.md
          PATCH_FILE_PATH: docs/incidents/issue-${{ needs.triage.outputs.issue_number }}.md
          PATCH_CONTENT_FILE: patch-note.md
        run: python tools/patch_proposal_runner.py
```

The Claude-calling steps (build a JSON payload, `curl` to `api.anthropic.com`, parse the
response) stay **bash + inline Python** exactly as in the .NET version's workflows — there
is no reason to change that part, it's already language-agnostic.

### 6. Repository configuration (these are NOT in any YAML — verify them explicitly)

Identical to the .NET version — none of this is Python-specific:

1. Set the repo secret: `gh secret set ANTHROPIC_API_KEY --repo <owner>/<repo>` — **never**
   ask the user to paste the key into chat; tell them to pipe it directly in their own
   terminal. If a key is ever pasted into a chat/session log, treat it as compromised and
   tell the user to revoke and rotate it immediately.
2. Fix the repo's default Actions token permission (defaults to **read-only** on many
   accounts):
   ```bash
   gh api -X PUT repos/<owner>/<repo>/actions/permissions/workflow \
     -f default_workflow_permissions=write \
     -F can_approve_pull_request_reviews=true
   ```
   Both flags are required — they fail with two *different* error messages if only one is
   set (`Resource not accessible by integration` vs `GitHub Actions is not permitted to
   create or approve pull requests`). Read the actual `gh run view --log-failed` output to
   tell which one is still missing rather than guessing.
3. Create every label the triage rules and workflows reference before the first real run:
   ```bash
   for l in "triage:ci-failure" "triage:needs-manual-review" "bug:null-reference" \
            "bug:timeout" "build:dependency" "build:compile-error" \
            "test:assertion-failure" "infra:resource-limit" "auth:credential-issue"; do
     gh label create "$l" --repo <owner>/<repo> --color "5319e7" --force
   done
   ```

### 7. Known bugs to avoid — these are language-agnostic, port them in advance

These were discovered the hard way building the .NET version of this exact project. They
are GitHub-platform and Anthropic-API bugs, not C#-specific, so build the Python version
with the fixes already in place instead of rediscovering them:

1. **Malformed JSON payloads to the Anthropic API.** Never build the request body via a
   bash heredoc with an embedded `$(python3 -c 'json.dumps(...)')` substitution inside an
   already-quoted JSON string — the substitution's own quotes corrupt the outer JSON. Since
   this project is Python-first, prefer building the payload directly with `python3 -c` or
   a small inline script that writes `payload.json` with `json.dump()`, then
   `curl -d @payload.json`. Always check `"content" in response` before indexing into it;
   print the raw API error and exit non-zero if it's missing, instead of crashing on a bare
   `KeyError`.
2. **`issues.labeled` never fires for bot-created issues.** GitHub blocks a workflow's own
   `GITHUB_TOKEN`-created events (like the label just added by `gh issue create --label`)
   from triggering other workflows — anti-recursion protection. Chain the patch-proposal
   logic as a second job in the *same* workflow file as the triage job (`needs: triage`,
   reading its `issue_number` output), not as a separate workflow triggered by
   `issues: labeled`. Keep a standalone label-triggered workflow only for the case where a
   *human* manually applies the label — that path does work.
3. **Runtime version mismatch between local dev and the CI runner.** The .NET version hit
   this as net6.0-vs-net8.0; the Python equivalent is pinning a different `python-version`
   locally than `actions/setup-python@v5` installs on the runner. Pin the same version
   (e.g. `3.12`) in both `pyproject.toml`/`requirements.txt` metadata and the workflow YAML,
   and verify with `python3 --version` before assuming a local pass means CI will pass too.
4. **Two independently-gated GitHub Actions permission settings**, not one. Fixing the
   workflow YAML's own `permissions:` block (`contents: write`, `pull-requests: write`) is
   necessary but not sufficient — the repo's default Actions token permission and the
   separate "Allow GitHub Actions to create pull requests" toggle both have to be set via
   `gh api`, as in section 6 above. Expect and diagnose both failures in that order rather
   than assuming one fix covers both.

### 8. Verification — do not claim any workflow works without proof

Same procedure as the .NET version:
1. Open a real PR against `main` with a small code change. Watch
   `gh run list --workflow "Claude PR Review"` reach `completed/success`, then
   `gh pr view <n> --comments` and confirm Claude's comment actually posted with real
   content.
2. Push a commit that deliberately breaks the build (e.g. a real `SyntaxError` or an
   `assert False` in a test) on a branch, open a PR so `build-and-test.yml` fails, then
   watch the chained `Claude CI Failure Triage` workflow. Confirm with
   `gh issue list --label "triage:ci-failure"` that a real issue appeared, and
   `gh pr list --draft` that a real draft PR appeared referencing it.
3. If any run fails, **read the actual failed step's log** (`gh run view <id>
   --log-failed`) before changing anything.
4. Once verified, revert or clean up the intentionally broken test file and any demo
   branches/PRs used purely for verification.

### 9. Documentation to produce alongside the code

- `README.md` — project layout, required repo setup (secret + permissions), how to build/
  test locally (venv activation, `pip install`, `pytest`), and the safety rails.
- `SETUP.md` — a literal log of every tool installed, credential configured, and repo
  setting changed, in the order it was done.
- `EXECUTION.md` — practical "how to run and test this" guide.

### 10. Safety rails — do not deviate from these

- Claude-authored changes never push directly to `main` and never auto-merge anywhere in
  the repo — every patch proposal lands as a **draft** PR requiring human review.
- Never enter, echo, or persist an API key, token, or password on the user's behalf — if
  one appears in chat, say so and tell the user to rotate it, don't use it.
- Creating a new public repo, pushing to a shared branch, and changing repo-wide security
  settings are all side-effecting actions — confirm with the user before doing them.

## PROMPT ENDS HERE

---

*This is a Python port of `PROMPT.md`, the prompt that built the .NET version of this
project in this same repo. Section 7's known bugs are copied forward deliberately — they
are platform-level (GitHub Actions, Anthropic API), not language-level, so they apply
whether the implementation is C# or Python. See `CONVERSATION_LOG.md` for the original
session where each one was actually hit and diagnosed.*
