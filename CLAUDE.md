# Project Rules

<!-- This file is yours — add whatever guidance you need for your project. -->
<!-- The sections below are suggestions from the github-workflow plugin.   -->
<!-- Edit, reorder, or remove anything that doesn't fit your workflow.     -->

## Tech Stack & Build

This is a .NET 10 solution; `Mutation.Ui` is a **WinUI 3** project (not WPF).
See `AGENTS.md` for the authoritative build/test commands and conventions
(tabs not spaces, `dotnet build` over `msbuild`, log redirection to `logs/`).

Quality gate before each commit: `dotnet test --configuration Release`.

## General Rules

Code implementation only. Do not provision accounts, configure
third-party services, set up DNS, or perform manual infrastructure
steps. Flag those as requiring human action.

The **GitHub issue** is the source of truth for every story. Read the
issue body first. Only consult reference docs for cross-cutting
concerns not covered in the issue.

## Autonomous Execution

Execute the full story workflow end-to-end without pausing for
confirmation. Skills are planning aids — consume their output and
continue to implementation. Never stop to ask "Ready to implement?"

## Story Execution

Work on **one story at a time** in a **fresh session per story**.
Complete it (PR created) or mark it blocked before starting the next.

### Build Principles

- One responsibility per file.
- Domain must not import from infrastructure. Strict layer boundaries.
- Every module unit-testable in isolation. Inject dependencies.
- Search for existing utilities before creating new ones.
- Write tests alongside the code, not after.

### Chaining Stories

When a story depends on another unmerged story:

1. Build the dependency on its own branch from the default branch.
2. Branch the dependent story off the dependency branch.
3. Set the dependent PR's base to the dependency branch.
4. After merge, rebase onto the default branch and update the PR base.

## Bug, Security, and Maintenance Workflow

When a bug, security issue, architecture violation, or tech debt is found during
development:

- **Trivial and same scope**: Fix in the current PR.
- **Everything else**: Run `/github-workflow:report-issue` to create
  a GitHub issue. Never silently skip problems.
- **Blocks current story**: Fix it first on its own branch.

## Accessibility

This app is used by a blind developer with ZoomText; screen-reader
accessibility is a core requirement for all UI work. Prefer configurable
controls exposed in the UI over hidden simplified defaults.

## Session Hygiene

- Start a **new session** for each story.
- Target **~100k tokens per session**. One story, one session. Commit
  and push progress early so work survives session boundaries.
- If a story is too large for one session, implement the most important
  slice, open a PR for it, and create follow-up issues for the rest.
- When compacting, preserve: modified files list, current test status,
  story number, branch name, and any blockers found.

## Supplementary Files

These files provide context for specific workflows. You don't need to
read all of them every session — consult them when the topic is
relevant to what you're working on.

| File | When to consult |
| ---- | --------------- |
| `ClaudeProject.md` | Project identity, labels, quality gate, branch convention, board config. Read at the start of any workflow command. |
| `AGENTS.md` | Tech stack (.NET 10, WinUI 3), build & test commands, code conventions. |
| `docs/review.config.md` | Review label definitions, non-compliance gates, tech-stack review rules. Read when performing or preparing for code review. |
| `.claude/ecosystem.md` | Installed Claude Code companion tool cheat-sheet — graphify queries, cost tracking, security scanning. |

Add your own reference docs to this table as needed — architecture
decisions, coding standards, API specs, etc. — so future sessions
know where to look.
