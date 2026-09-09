# Prompt for OpenAI Codex — run the existing dotnet-claude-cicd project

Paste everything between the START/END markers into Codex (CLI or IDE) in a session with
shell access. Unlike PROMPT.md (which rebuilds the project from scratch), this prompt
**runs the project that already exists** at
`https://github.com/venkatesh-db/dotnet-claude-cicd` and walks Codex through every real
bug that was hit and fixed while getting it green, so it doesn't have to rediscover them.

---

## PROMPT STARTS HERE

You are setting up and running an existing .NET 8 project that wires Claude into GitHub
Actions for PR review, CI-failure triage, and patch proposals. The repo already exists and
is already working — your job is to get it running in **this** environment and verify it,
not redesign it. Read this whole prompt before running anything; it lists every real bug a
previous session hit so you don't waste turns rediscovering them.

### Step 1 — Clone

```bash
git clone https://github.com/venkatesh-db/dotnet-claude-cicd.git
cd dotnet-claude-cicd
```

If you don't have push access, ask the user for a fork URL or for collaborator access
before doing anything that pushes.

### Step 2 — Install .NET 8 SDK (do NOT assume sudo is available)

Check first:
```bash
dotnet --list-sdks
```

If there's no `8.x` entry, **do not** reach for a package-manager cask/installer that needs
admin rights — Codex sandboxes commonly can't prompt for a password interactively and the
install will hang or fail silently. Use Microsoft's official user-local script instead,
which needs no elevated privileges:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
dotnet --version   # should print 8.x
```

Every `dotnet` command below assumes `$HOME/.dotnet` is on `PATH` for this session. If you
want it to persist across sessions, append the export to the shell profile — but don't do
that without asking, since it changes the user's environment permanently.

### Step 3 — Build and test

```bash
dotnet restore
dotnet build     # expect: 0 warnings, 0 errors (all projects target net8.0)
dotnet test      # expect: 6 tests passing in GithubIntegration.Tests
```

If you see NETSDK1138 warnings about `net6.0` being out of support, or a runtime-launch
error like "You must install or update .NET to run this application," **stop** — that means
a `.csproj` somewhere reverted to `net6.0`. Every project (`GithubIntegration.Api`,
`GithubIntegration.Core`, `GithubIntegration.GitHubClient`, `GithubIntegration.Tests`,
`PatchProposalRunner`) must target `net8.0`. This exact bug took down the CI-triage
workflow's `propose-patch` job in production — see Known Issue #3 below.

### Step 4 — Run the API project (proves the solution actually executes, not just compiles)

```bash
dotnet run --project src/GithubIntegration.Api --urls "http://localhost:5299" &
sleep 5
curl -s http://localhost:5299/weatherforecast
# then stop it
pkill -f "GithubIntegration.Api"
```

You should get back a real JSON array. This project is a scaffold — nothing in the
production workflows calls it yet — but confirming it boots is a legitimate smoke test.

### Step 5 — GitHub CLI, if you need to touch Actions/issues/PRs

```bash
gh --version || brew install gh    # or apt/winget equivalent for your OS
gh auth login --hostname github.com --git-protocol https --web --scopes "repo,workflow"
```

**The `workflow` scope is mandatory.** Authenticating with plain `repo` scope will let you
clone and read, but GitHub will reject any push that touches `.github/workflows/*` with a
scope error. This device-code flow needs a human to open a URL and approve in a browser —
if Codex is running fully unattended, surface the code/URL and pause for the user rather
than guessing you're authenticated.

### Step 6 — Repo-level configuration this project depends on (NOT in any YAML)

These are GitHub repo settings, invisible in the codebase, and the workflows silently fail
without them. Check and fix each one — do not assume they're already set on a fork or a
newly-created repo:

```bash
# 1. The Anthropic API key the Claude-calling steps need
gh secret list --repo <owner>/<repo>
# if ANTHROPIC_API_KEY is missing, tell the user to set it themselves, in their own
# terminal — NEVER ask them to paste the key into this chat/session, and never type it
# yourself even if they offer it:
#   echo "<key>" | gh secret set ANTHROPIC_API_KEY --repo <owner>/<repo>

# 2. Default Actions token permission (defaults to read-only on many accounts)
gh api repos/<owner>/<repo>/actions/permissions/workflow
# if "default_workflow_permissions" is "read", fix it:
gh api -X PUT repos/<owner>/<repo>/actions/permissions/workflow \
  -f default_workflow_permissions=write \
  -F can_approve_pull_request_reviews=true
# both flags matter — see Known Issue #4 below, they fail with two DIFFERENT error
# messages and fixing only one still leaves the pipeline broken

# 3. Labels the triage workflow applies (gh issue create --label fails hard if missing)
for l in "triage:ci-failure" "triage:needs-manual-review" "bug:null-reference" \
         "bug:timeout" "build:dependency" "build:compile-error" \
         "test:assertion-failure" "infra:resource-limit" "auth:credential-issue"; do
  gh label create "$l" --repo <owner>/<repo> --color "5319e7" --force
done
```

### Step 7 — Verify each of the three pillars for real (don't just read the YAML and assume)

Do not report any of these as "working" without watching an actual run:

**PR review** — open a real PR against `main`, then:
```bash
gh run list --repo <owner>/<repo> --workflow "Claude PR Review" --limit 1
gh run view <run-id> --repo <owner>/<repo> --log-failed   # only if it failed
gh pr view <pr-number> --repo <owner>/<repo> --comments    # confirm the comment posted
```

**CI-triage + patch-proposal** — push a commit that deliberately breaks the build (a real
compile error, e.g. calling an undefined method), open a PR so `build-and-test.yml` fails,
then:
```bash
gh run list --repo <owner>/<repo> --workflow "Claude CI Failure Triage" --limit 1
gh issue list --repo <owner>/<repo> --label "triage:ci-failure"   # confirm the issue opened
gh pr list --repo <owner>/<repo> --draft                          # confirm the draft PR opened
```
Revert the intentionally-broken file once you've confirmed the chain works.

If a run fails, **read the actual log with `--log-failed` before changing anything.** Do
not guess at a fix.

### Known issues a previous session already hit and fixed — read before debugging

If you hit any of these, the fix is already known; apply it directly instead of
re-diagnosing from scratch.

1. **Malformed JSON sent to the Anthropic API.** An earlier version of the workflows built
   the request body with a bash heredoc containing an embedded
   `$(python3 -c 'json.dumps(...)')` substitution inside an already-quoted JSON string —
   this produces invalid JSON with doubled quotes, and every Claude call failed with
   `KeyError: 'content'` when parsing the response. **Current fix (already in the repo):**
   the payload is built as a real Python dict and serialized with `json.dump()` to a file,
   then `curl -d @payload.json`. If you see this error again, check whether that pattern
   regressed.

2. **`issues.labeled`-triggered workflow never fires for bot-created issues.** GitHub
   blocks a workflow's own `GITHUB_TOKEN` activity (like the label added when
   `gh issue create --label` runs) from triggering other workflows — this is
   anti-recursion protection, not a bug you can configure around. **Current fix:** the
   patch-proposal logic is chained as a second job (`propose-patch`, `needs: triage`)
   directly inside `claude-ci-triage.yml`, not a separate workflow. A standalone
   `claude-patch-proposal.yml` triggered on `issues: labeled` is kept only for the case
   where a *human* manually applies the label — that path does work.

3. **`net6.0` vs `net8.0` runtime mismatch.** Projects scaffolded against whatever SDK
   happens to be locally installed can end up targeting `net6.0`, but
   `actions/setup-dotnet@v4` with `dotnet-version: "8.0.x"` installs no `net6.0` runtime on
   the CI runner — a `net6.0` binary crashes at launch with "You must install or update
   .NET to run this application." **Current fix:** every `.csproj` targets `net8.0`. If you
   add a new project, target `net8.0` from the start.

4. **Two separate, independently-gated permission failures**, discovered in this order:
   - `Octokit.ForbiddenException: Resource not accessible by integration` — caused by the
     repo's default Actions token permission being `read` (a repo setting, not a workflow
     setting) regardless of what the workflow's own `permissions:` block requests.
   - After fixing that: `GitHub Actions is not permitted to create or approve pull
     requests` — a *second*, independent toggle ("Allow GitHub Actions to create pull
     requests"). Fixing only the first does not fix the second.
   **Current fix:** both are set via the single `gh api -X PUT
   .../actions/permissions/workflow` call in Step 6 above, with both flags.

5. **`.DS_Store` files got committed** on macOS during development. `.gitignore` already
   has `.DS_Store` — if you're on macOS, don't disable this or commit through it.

6. **A guessed URL for the products/docs path 404s.** Not a project bug, just a reminder:
   don't assume conventional paths exist — verify with `find()`/`gh api`/an actual request
   before citing a URL in docs or code.

### Safety rails — do not change these without the user explicitly asking

- Nothing in this repo auto-merges. Every Claude-authored patch lands as a **draft** PR.
- Never push directly to `main` on Claude's behalf.
- Never type, echo, or persist an API key or token on the user's behalf, even if they paste
  one into the session — tell them to rotate it and set it themselves in their own
  terminal.

## PROMPT ENDS HERE

---

*Companion to `PROMPT.md` (rebuild-from-scratch) and `SETUP.md` /
`CONVERSATION_LOG.md` (what was actually done and why) in this repo.*
