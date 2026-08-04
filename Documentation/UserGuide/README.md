# User guide — how this folder works

This folder holds the Mutation user guide.

```
Documentation/
	Build-UserGuide.cmd            <- double-click this to rebuild the HTML
	UserGuideBuilder/              <- the converter, a small .NET console project
		Program.cs                 <- command line and orchestration
		GuideBuilder.cs            <- builds every chapter, clears stale pages
		ChapterDiscovery.cs        <- finds chapters, reading order, titles
		MarkdownRenderer.cs        <- Markdig pipeline and the guide's own rules
		PageTemplate.cs            <- assembles nav, body and footer into a page
		Assets/userguide-template.html  <- the page shell
		Assets/userguide.css            <- styling, inlined into every page
	UserGuide/
		markdown/                  <- the source of truth. Edit these.
		html/                      <- generated. Do not edit by hand.
		README.md                  <- this file (not part of the guide)
```

## The Markdown is authoritative

`UserGuide/markdown/*.md` is the guide. The pages in `UserGuide/html/` are generated
output, checked in so the app can open the guide locally without a build step. If you
change the guide, change the Markdown and re-run the build — any hand edit to an HTML
file is lost on the next run.

## Rebuilding the HTML

Double-click `Documentation\Build-UserGuide.cmd`, or run it from a prompt:

```bash
Documentation\Build-UserGuide.cmd
```

Commit the regenerated `UserGuide/html/` alongside your Markdown change so the two
never drift.

**Everything needed is in this repository.** The converter is the `UserGuideBuilder`
project; the only thing you need installed is the .NET SDK, which you already have if
you can build Mutation itself. There is no separate tool to download and nothing to
put on your PATH.

You can also run it directly, which is handy when working on the converter:

```bash
dotnet run --project Documentation/UserGuideBuilder
```

It accepts `--markdown`, `--html` and `--site-title` if you ever need to point it
somewhere else. With no arguments it finds the guide by walking up from the
executable, so the working directory does not matter.

## Adding a chapter

1. Add `UserGuide/markdown/your-chapter.md`, starting with a single `# Title` line.
   That heading becomes the page title and the sidebar label.
2. Link to it from `UserGuide/markdown/index.md` so readers can find it.
3. Add its file name (without `.md`) to `ChapterDiscovery.ReadingOrder` so it lands in
   the right place in the sidebar. Chapters that are not listed still build — they are
   just appended alphabetically.
4. Run the build.

Renaming or deleting a chapter is safe: the build deletes orphaned HTML files.

A file whose name starts with `_` is ignored, which is useful for drafts.

## Markdown flavour

Chapters are parsed by [Markdig](https://github.com/xoofx/markdig) as CommonMark plus
the GitHub extensions people actually reach for: tables, task lists, strikethrough,
autolinks, and heading anchors.

Three project-specific rules are applied on top:

- Links to `something.md` are rewritten to `something.html`, so chapters can link to
  each other and stay correct both on GitHub and in the built site.
- Table header cells get `scope="col"`, and each table is wrapped in a scrolling
  `div` — see the note on table semantics below.
- **Raw HTML is escaped, not passed through.** The page shell carries the landmarks a
  screen reader navigates by, and one unclosed tag in a chapter could break that
  nesting for every reader of the page. Write Markdown, not HTML.

## The generated pages

Each page is standalone — the stylesheet is inlined at build time, so there are no
external files or network requests, and `UserGuide/html/index.html` opens correctly
straight off disk.

The output is built to be accessible, which matters for this project specifically:
`lang` set, a skip-to-content link, a labelled `<nav>` landmark with `aria-current` on
the current page, a `<main>` landmark, headings in document order, `scope="col"` on
every table header, visible focus outlines, and a light/dark theme that follows the
reader's Windows setting. There is also a print stylesheet that drops the navigation.

The output is also **reproducible**: rebuild on a different day and only pages whose
Markdown changed come out different. That is deliberate, because the pages are
committed — a build date in every footer would rewrite all 13 on the first rebuild of a
new day, burying the real edits and colliding between branches. So only `index.html`
carries the "Generated from the Markdown source on …" line; every other page keeps the
"do not edit by hand" warning without a date. Line endings are forced to LF for the
same reason. If you add anything else that varies per build, put it on the contents
page too, or leave it out.

> **Why tables are wrapped in a div.** A wide table has to scroll sideways in a narrow
> window — including at high ZoomText magnification, which shrinks the usable viewport
> the same way. Doing that with `table { display: block }` costs the element its
> implicit table semantics in several browser and screen-reader combinations, so rows
> and columns stop being announced as a table. Scrolling the wrapper instead leaves
> the table a table.

To change how pages look, edit `Assets/userguide.css`. To change their structure, edit
`Assets/userguide-template.html` — the `{{TOKENS}}` in it are filled in by
`PageTemplate.cs`. Both are embedded into the tool at build time, so just rebuild.

## Tests

The converter is covered by `Mutation.Tests/UserGuideBuilderTests.cs`, which runs as
part of the normal suite:

```bash
dotnet test --configuration Release
```

## Linking the guide from the app

The pages are self-contained, so opening `UserGuide/html/index.html` in the default
browser is all that is needed. Nothing in the app depends on this folder yet.
