# Project Configuration

Settings for the `github-workflow` plugin. All commands and the execute
skill read this file. Keep it lean — it is auto-loaded into context on
every workflow command, so prefer values over prose and remove sections
your project does not use.

## Identity

| Setting        | Value                  |
| -------------- | ---------------------- |
| org            | `Subverting-complexity` |
| repo           | `Mutation`             |
| default-branch | `main`                 |

## Package Manager

`dotnet`

## Quality Gate

Command to run before each commit:

```
dotnet test --configuration Release
```

## Branch Convention

Pattern for feature branches:

```
feature/{number}/{short-desc}
```

Example: `feature/123/fix-deepgram-key-warning`

## Label Map

Map workflow purposes to your repository's actual label names. State
machine, transitions, and defaults: `templates/default-labels.md`.

### Priority

| Purpose           | Label               |
| ----------------- | ------------------- |
| priority-critical | `priority-critical` |
| priority-high     | `priority-high`     |
| priority-medium   | `priority-medium`   |
| priority-low      | `priority-low`      |

### Type

(Fallback classification for the rare non-type-capable path; this org
uses native GitHub issue types — see Issue Types & Fields below.)

| Purpose       | Label           |
| ------------- | --------------- |
| type-story    | `type-story`    |
| type-bug      | `type-bug`      |
| type-security | `type-security` |
| type-debt     | `type-debt`     |
| type-arch     | `type-arch`     |

### Status (issue lifecycle)

Every issue carries exactly one of these lifecycle labels (the issue-side
mirror of the PR review-state machine).

| Purpose                | Label                    |
| ---------------------- | ------------------------ |
| status-ready           | `status-ready`           |
| needs-refinement       | `needs-refinement`       |
| status-in-progress     | `status-in-progress`     |
| status-parked          | `status-parked`          |
| status-blocked         | `status-blocked`         |
| status-in-review       | `status-in-review`       |
| status-needs-attention | `status-needs-attention` |

### Claude

`claude-authored` is a provenance marker (not a lifecycle state) applied to
Claude-authored PRs and Claude-created issues. Agent gating is disabled, so
no `claude-ready` approval label is used. (PR review-state labels are
separate — see `docs/review.config.md`.)

| Purpose          | Label             | Applied by                                               |
| ---------------- | ----------------- | -------------------------------------------------------- |
| claude-authored  | `claude-authored` | finish-story (PRs), report-issue / finish-story (issues) |

## Issue Types & Fields

This org has **native GitHub issue types** (Bug, Feature, User Story, Epic)
and **org issue fields**, so the workflow uses them as first-class
classification/metadata instead of labels. Capability is auto-detected per
dimension at runtime (`templates/issue-fields-resolution.md`). The
purpose→value mappings live in `templates/default-labels.md`. All expected
fields exist in this org.

| Purpose key          | Field name      |
| -------------------- | --------------- |
| field-priority       | `Priority`      |
| field-effort         | `Effort`        |
| field-type           | `Classification` |
| field-origin         | `Origin`        |
| field-start          | `Start date`    |
| field-target         | `Target date`   |
| field-parent         | `Parent`        |
| field-status-reason  | `Status reason` |

## Ready Gate

| Setting    | Value          |
| ---------- | -------------- |
| ready-gate | `board-column` |

Stories signal eligibility for pickup via the **Ready** column on the
project board. (A board with a "Ready" option is required and configured
below.)

## Agent Gating

| Setting       | Value      |
| ------------- | ---------- |
| agent-gating  | `disabled` |

Any eligible unassigned issue (in the Ready column) can be picked without a
separate human approval label.

## Refinement

| Setting          | Value               |
| ---------------- | ------------------- |
| refinement-skill | `feature-discovery` |

Skill the execute flow offers when a `needs-refinement` story is next:
`feature-discovery` (default, code-aware spec+AC) or `grill-me`
(lightweight Q&A, no codebase exploration).

## Session Budget

Target ~100k tokens per session. One story per session, run
start-to-finish. Commit and push early so work survives an unexpected end.

## Story Template

Issues should include at minimum: **Context** (what/why), **Requirements**
(acceptance criteria + constraints), and optionally **Notes**
(dependencies, references, edge cases).

## Issue Prefixes

| Type         | Prefix       |
| ------------ | ------------ |
| Story        | `[STORY]`    |
| Bug          | `[BUG]`      |
| Security     | `[SECURITY]` |
| Architecture | `[ARCH]`     |
| Tech Debt    | `[DEBT]`     |

## Project Board

`project-title` is re-checked against `project-node-id` before any board
write, so a stale id fails loudly instead of mutating the wrong board.

| Setting             | Value                      |
| ------------------- | -------------------------- |
| project-number      | `6`                        |
| project-title       | `Mutation`                 |
| project-node-id     | `PVT_kwDODj6aos4BXMxc`     |
| status-field-id     | `PVTSSF_lADODj6aos4BXMxczhSbT8U` |
| start-date-field-id | `N/A`                      |
| end-date-field-id   | `N/A`                      |

### Status Options

| Status      | Purpose key       | Option ID  |
| ----------- | ----------------- | ---------- |
| Backlog     | `col-backlog`     | `f75ad846` |
| Ready       | `col-ready`       | `6bff5a53` |
| In Progress | `col-in-progress` | `47fc9ee4` |
| In Review   | `col-in-review`   | `211e0ac6` |
| Blocked     | `col-blocked`     | `311b4541` |
| Done        | `col-done`        | `98236657` |

## Reference Docs

- `AGENTS.md` — tech stack (.NET 10, WinUI 3), build & test commands, conventions
- `docs/review.config.md` — review labels, gates, tech-stack review rules

## Bundled Skills

Available as `/github-workflow:*`: code-architect (planning),
structured-coding (implementation), code-review (review/audit), grill-me
(plan validation), feature-discovery (backlog creation), repo-scaffolding
(project setup).
