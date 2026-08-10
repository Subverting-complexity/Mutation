# Project Rules

<!-- This file is yours — add whatever guidance you need for your project. -->
<!-- The sections below are suggestions from the github-workflow plugin.   -->
<!-- Edit, reorder, or remove anything that doesn't fit your workflow.     -->

## Communication Style

These rules govern everything you write for me to read: chat replies,
status updates, questions you ask me, and the summary at the end of a
piece of work. They do not govern code comments, commit messages or pull
request bodies, which have a different audience and their own conventions.
They also do not govern the user guide, which is written for someone who
is not a developer and has its own tone rules under **Documentation**
below.

### Simplified Technical English, plus Zinsser

Write in Simplified Technical English (ASD-STE100) where practical, and
follow William Zinsser's four principles of good writing: simplicity,
brevity, clarity, humanity.

- Use short, direct sentences and the active voice.
- Use simple, precise words. Give each technical term one meaning and keep
  that meaning everywhere.
- Cut unnecessary words, repetition, and clutter.
- Make instructions and explanations easy to understand the first time.
- Keep the writing natural and human. Do not sound mechanical or like a
  technical manual unless the task asks for that style.
- Keep the technical accuracy, terminology, nuance, and detail that the
  content needs. Brevity is not a licence to leave things out.
- When elegant wording and easy comprehension conflict, choose easy
  comprehension.
- Avoid jargon, idioms, metaphors, and complex sentence structures unless
  accuracy needs them.

### Explain, do not just label

Explain things the way you would to a capable colleague who does not know
this codebase and is not reading the diff alongside you.

- Lead with what actually happens, in plain words, before naming the file,
  function or library it happens in. A name is a label for something I
  already understand, not a substitute for understanding it.
- Prefer a short narrative to a list of identifiers. Where a change has a
  cause, a consequence and a trade-off, say all three as sentences.
- Write in full sentences. Do not compress into headline fragments or
  strings of bare identifiers.
- Where something is subtle, say why it is subtle and what would go wrong
  if it were done the obvious way. The failure a design prevents is
  usually the clearest explanation of it available.
- Say what a user of this software would actually notice. "Nothing changes
  for the user" is a useful sentence and worth writing.
- Be explicit about what you did not verify, and about what cannot be
  verified in an agent session at all, rather than leaving me to infer it
  from what you left out.

### Name things, never cite a bare number

Never refer to a work item by bare number or ID. Every mention of an issue
or pull request carries a short plain-English description of what it is:
"the quality-gate build-integrity issue (#437)", not "#437". Lead with the
human-readable name and put the number in parentheses as a lookup key,
never the reverse.

This matters most in lists of candidates, where a run of bare numbers is
unreadable noise. I do not carry the number-to-topic mapping in my head.
In tables, give titles their own column. The same goes for milestone
titles, branch names and label sets: describe the thing, then cite the
identifier. It holds for everything you file in passing too: write "filed
bug #45 about the stuck progress bar", not "filed bug #45", even where a
plugin or skill shows you the bare-number form.

### End a finished piece of work with a clear closing summary

When you finish a chunk of work, end your reply with a plain summary of
what happened. A chunk of work means the whole run: you built it, you
opened the pull request, you reviewed it, you made the fixes, and you
merged it.

Write one to three paragraphs in simple English. Say which stories are
finished, by number and title, and say what was actually done in each, in
terms of what the software now does. Do not make me read back through the
session to work out where things landed.

The last paragraph must say that the merge was done. Say it plainly, in
its own sentence, near the end: which pull request merged, into which
branch, and that it is now on `main`. If a merge did not happen, say that
just as plainly and say what is blocking it.

### Where these rules meet the project's own conventions

Two places in this repository push the other way. These rules win in both,
with one narrow exception.

The `github-workflow` plugin, its skills, and this repository's own board
tooling present work items as bare numbers, and the branch convention in
`ClaudeProject.md` builds a branch name out of one
(`feature/{number}/{short-desc}`). In prose you write for me, name the
thing and put the number in brackets, whatever form the tool showed you.
The exception is a literal identifier a machine consumes — a `gh` command
argument, a branch name, a URL, a file path. There the number is the
value, so use it as-is and describe it in the surrounding sentence.

The **Autonomous Execution** rule below says never to stop and ask
permission to continue. That governs whether you ask, not how you write.
When a question is genuinely necessary, ask it in the style above.

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

### Claim the story first

**Before reading any code**, for every story you pick up:

1. `gh issue edit <n> --add-assignee @me --add-label status-in-progress --remove-label status-ready`
2. Move its card to **In Progress** on the board.

Then start. Not after planning, not at PR time — first.

**An assigned issue is taken.** Pick a different one, whatever the board
column says. The Ready column is not a claim, and neither is a lifecycle
label on its own.

More than one session runs against this repo at once, and the board is the
only thing they share. On 2026-08-08 three sessions each picked the same
three issues out of Ready — the router's From and To boxes rewriting
themselves silently (#332), a core hotkey and a router mapping both able to
claim the same chord (#321), and Deepgram never retrying a server-side
failure (#316) — built the same fixes, and raced to merge: two sessions'
work was thrown away, and the issues went from `status-ready` straight to
closed with no session ever having claimed them. Claiming costs one
command. Not claiming cost most of a session, twice.

This applies whether or not you invoke a workflow skill. Going straight from
the board to the code is exactly the path that skips the claim — which is
how it happened.

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

## Documentation

The user guide lives in `Documentation/UserGuide/markdown/`. It is the
source of truth; `Documentation/UserGuide/html/` is generated output.
See `Documentation/README.md` for the folder layout and the build.

### Keep it in sync — same PR, every time

**Whenever behaviour a user can see changes, update the guide in the same
pull request.** Never leave it for later; a guide that lies is worse than
no guide. That includes:

- New features, or features removed.
- Changed default hotkeys, setting names, ranges, or defaults.
- Renamed or moved buttons, cards, menus, or settings pages.
- Changed messages, beeps, or announcements the user hears or reads.
- New failure modes worth a `troubleshooting.md` entry.

Then rebuild and commit the HTML:

```bash
Documentation\Build-UserGuide.cmd
```

Markdown and generated HTML must be committed together so they never
drift. Add a new chapter by following `Documentation/UserGuide/README.md`,
and link it from `index.md`.

**Verify against the code, not the README.** Root `README.md` has drifted
from the shipped defaults before. `Mutation.Ui/Views/Settings/SettingsDefaults.cs`
is authoritative for defaults, and the XAML is authoritative for what a
control is actually labelled on screen.

### Tone — plain, friendly, and for a general office worker

The reader is **not** a developer. Write for someone who has never heard
the words "API", "regex", or "endpoint".

- Short sentences, one idea each. Address the reader as "you".
- Warm and helpful, like a colleague showing someone the ropes. Never
  stiff, never salesy, never patronising.
- Explain any unavoidable technical term in plain words the first time it
  appears, in the same sentence — for example, "an API key (a long
  password that lets Mutation talk to the service on your behalf)".
- Prefer a concrete example over an abstract description. Say what the
  reader does, and what they see or hear happen.
- Say it once, briefly. If a feature is simple, two lines is the right
  length. No padding, no marketing language, no "leverage" or "seamlessly".
- Never invent behaviour. Everything must be traceable to the code. If you
  are unsure, leave it out rather than guess.
- Bold for keyboard shortcuts (**Ctrl+Shift+M**) and for on-screen names,
  matching the label the app actually shows.
- Make clear that default shortcuts can be changed.
- End each chapter with a short **Where to next** section linking to the
  2–4 most related chapters.

Write Markdown, not HTML — raw HTML is escaped by the build on purpose, so
a stray tag cannot break the screen-reader landmarks in the page.

## Accessibility

This app is used by a blind developer with ZoomText; screen-reader
accessibility is a core requirement for all UI work. Prefer configurable
controls exposed in the UI over hidden simplified defaults.

This extends to generated documentation: the guide's HTML keeps proper
landmarks, heading order, table header scope, and visible focus outlines.
Do not regress those when changing the page template or stylesheet.

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
| `Documentation/README.md` | User guide folder layout, which files are authoritative, and how to regenerate the HTML. Read whenever a change affects what users see. |
| `Documentation/UserGuide/README.md` | Adding a chapter, supported Markdown, how the generated pages stay accessible. |
| `.claude/ecosystem.md` | Installed Claude Code companion tool cheat-sheet — graphify queries, cost tracking, security scanning. |

Add your own reference docs to this table as needed — architecture
decisions, coding standards, API specs, etc. — so future sessions
know where to look.
