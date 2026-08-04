# Documentation

Everything written for people who *use* Mutation lives here. Notes for people who
*build* Mutation live in `AGENTS.md` and `CLAUDE.md` at the root of the repository.

## What is in each folder

| Folder or file | What it holds |
|---|---|
| `UserGuide/markdown/` | **The user guide itself.** One Markdown file per chapter. This is the source of truth — edit these. |
| `UserGuide/html/` | The same guide as web pages. **Generated. Never edit by hand.** Checked in so the app can open the guide without a build step. |
| `UserGuide/README.md` | Notes for whoever maintains the guide: how to add a chapter, which Markdown features are supported, how the pages are made accessible. |
| `UserGuideBuilder/` | The small .NET program that turns the Markdown into the web pages. |
| `Build-UserGuide.cmd` | Double-click this to regenerate the web pages. |

## The Markdown is the authority

`UserGuide/markdown/*.md` is the real guide. The HTML in `UserGuide/html/` is output —
it is produced from the Markdown every time the build runs.

That means two rules, and they matter:

1. **Change the Markdown, never the HTML.** Any edit made directly to an HTML file is
   silently thrown away the next time someone builds.
2. **Commit the regenerated HTML with your Markdown change**, so the two never drift
   apart. The app opens the HTML, so stale HTML means users read stale instructions.

## Generating the HTML

Double-click `Build-UserGuide.cmd`, or run it from a prompt:

```bash
Documentation\Build-UserGuide.cmd
```

That is the whole process. It rewrites every page in `UserGuide/html/` and reports what
it wrote.

Everything needed is in this repository. The only thing you need installed is the .NET
SDK, which you already have if you can build Mutation itself — there is no separate
tool to download and nothing to put on your PATH.

If you are working on the converter, you can also run it directly:

```bash
dotnet run --project Documentation/UserGuideBuilder
```

The converter is covered by tests that run with the normal suite:

```bash
dotnet test --configuration Release
```

## Reading the guide

Open `UserGuide/html/index.html` in any browser, or click **User guide** in the top
right of Mutation's main window.

Each page is self-contained — the styling is built into the file — so the pages work
straight off disk with no internet connection and no other files needed.

## Keeping the guide honest

When you change how Mutation behaves, update the guide in the same pull request. The
rules on when and how are in [`CLAUDE.md`](../CLAUDE.md) under **Documentation**; the
short version is that the guide is written in plain, friendly language for an ordinary
office worker, and it should stay that way.
