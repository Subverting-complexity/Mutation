# Review Configuration — Mutation

## Repository

- Org: Subverting-complexity
- Repo: Mutation
- Default branch: main

## Labels

State labels use the `review` prefix so they're easy to filter. The
**Purpose** column is the stable identity skills resolve against — it never
changes even when the prefix does.

State labels are mutually exclusive — exactly one is applied per review.

| Purpose | Label | Type | Meaning |
| ------- | ----- | ---- | ------- |
| `needs-review` | `review-needs-review` | State | Open PR awaiting its first review (entry state, applied at creation) |
| `reviewing` | `review-reviewing` | State | Review in progress — prevents concurrent reviews |
| `approved` | `review-approved` | State | No remaining issues, ready for merge |
| `changes-requested` | `review-changes-requested` | State | Concrete problems remain that a human must address |
| `needs-discussion` | `review-needs-discussion` | State | Architectural or scope questions need human judgment |
| `needs-re-review` | `review-needs-re-review` | State | New commits pushed since last review — re-review required |
| `failed` | `review-failed` | State | Review could not be completed (checkout failed, PR too large) |
| `updating` | `review-updating` | State | A builder agent is addressing review feedback — prevents concurrent updates |
| `fixes-applied` | `review-fixes-applied` | Action | Claude pushed fix commits to the PR branch (sticky across runs) |

These labels are managed by the `/github-workflow:code-review` skill
and form the single source of truth for PR review state. Claude labels
in `ClaudeProject.md` (like `claude-authored`) are separate workflow
markers that do not participate in this state machine.

## Auto-Merge on Approval

| Setting                 | Value        |
| ----------------------- | ------------ |
| auto-merge-on-approval  | `enabled`    |
| require-ci-before-merge | `if-present` |

When approved, the code-review skill squash-merges the PR (deleting its
branch) once the review verdict is **Approved** and the review comment is
posted.

`require-ci-before-merge: if-present` gates on CI **only when CI exists**:
if the PR's head commit has checks, they must be green before merge; if it
has no checks at all, it merges. This repo currently has **no CI pipeline**,
so approved PRs merge once Claude's review (which runs the local
`dotnet test --configuration Release` quality gate) approves them. The
moment a GitHub Actions workflow is added, those checks are automatically
gated before merge — no config change needed.

> Note: `if-present` is **not** an absolute gate while no pipeline exists.
> For a hard "never merge without green CI" guarantee, add a PR workflow and
> either flip this to `true` or set up GitHub-enforced required status checks
> (`/github-workflow:setup harden`).

Because Claude records approval as a review comment + the `review-approved`
label (not a GitHub *review*), the merging actor needs merge permission on
the repo for queued merges to land.

## Hard Non-Compliance Gates

Any of these force a `Changes Requested` verdict regardless of all other
findings.

- The solution does not build: `dotnet build --configuration Release` fails.
- Any test fails: `dotnet test --configuration Release` is not green.
- A UI change regresses screen-reader accessibility (missing/incorrect
  `AutomationProperties`, unlabeled controls, keyboard-trap, focus loss).
- New user-facing behavior or settings are hidden behind hardcoded defaults
  instead of being exposed as configurable controls in the UI.
- Secrets, API keys, or credentials committed to the repo.

## Tech Stack Review Rules

These are project-specific checks to run in addition to the generic review.

- **.NET 10 / WinUI 3** — `Mutation.Ui` is WinUI 3 (not WPF); reject
  WPF-only APIs or guidance. Prefer `dotnet build` over `msbuild`.
- Follow repo conventions in `AGENTS.md`: **tabs, not spaces**; redirect
  build/test output to `logs/`.
- Async UI work must not block the UI thread; use `async`/`await` and
  marshal back to the dispatcher for UI updates.
- `IDisposable` resources (audio, HTTP, streams) are disposed; long-lived
  event handlers are unsubscribed to avoid leaks.

## Architecture Rules

- Keep clear layer boundaries: UI (`Mutation.Ui`) depends on services
  (`CognitiveSupport`), not the reverse. Domain/service logic must not
  reference UI types.
- One responsibility per file; inject dependencies for testability.
- Search for existing utilities before adding new ones; avoid duplication.

## Security Specifics

- Provider API keys (speech-to-text, TTS, LLM) come from configuration, never
  hardcoded. A missing key surfaces a clear warning, not a crash.
- Validate and bound external input (audio buffers, API responses) before use.

## Test Expectations

- New service/logic code has unit tests in `Mutation.Tests`.
- Bug fixes include a regression test where practical.
- The full suite (`dotnet test --configuration Release`) passes before merge.

## Review Comment Footer

```
---
Reviewed at <SHA>
🤖 Reviewed with Claude Code
```

The `Reviewed at <SHA>` line is machine-parsed by future runs to detect
whether the PR has changed since the last review.
