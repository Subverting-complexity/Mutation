# User guide — how this folder works

This folder holds the Mutation user guide.

```
Documentation/
	Build-UserGuide.cmd        <- double-click this to rebuild the HTML
	Convert-UserGuide.ps1      <- the build script it calls
	userguide-template.html    <- page shell (skip link, nav slot, main landmark)
	userguide.css              <- styling, inlined into every page at build time
	userguide.lua              <- pandoc filter: .md links -> .html, table scope
	pandoc.exe                 <- the converter (not in git, see below)
	UserGuide/
		markdown/              <- the source of truth. Edit these.
		html/                  <- generated. Do not edit by hand.
		README.md              <- this file (not part of the guide)
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

## You need pandoc

Conversion is done by [pandoc](https://pandoc.org). The build looks for it in this
order:

1. `pandoc.exe` in the `Documentation` folder.
2. `pandoc` on your PATH.
3. Whatever you pass to `-Pandoc`, for example:

```bash
powershell -File Documentation\Convert-UserGuide.ps1 -Pandoc "C:\tools\pandoc.exe"
```

If none of those find it, the build stops with a message telling you where to put it.

**`pandoc.exe` is deliberately not committed.** It is around 220 MB, which is over
GitHub's 100 MB per-file limit, so a push containing it would be rejected outright.
It is listed in `.gitignore`. Download it once from
<https://pandoc.org/installing.html> and drop it in `Documentation\`, or install it
normally so it is on your PATH.

## Adding a chapter

1. Add `UserGuide/markdown/your-chapter.md`, starting with a single `# Title` line.
2. Link to it from `UserGuide/markdown/index.md` so readers can find it.
3. Add its file name (without `.md`) to `$chapterOrder` near the top of
   `Convert-UserGuide.ps1` so it lands in the right place in the sidebar. Chapters
   that are not listed still build — they are just appended alphabetically.
4. Run the build.

Renaming or deleting a chapter is safe: the build removes orphaned HTML files.

## Markdown flavour

Pages are parsed as GitHub Flavored Markdown, so anything that renders on GitHub
renders here: headings, lists, tables, fenced code, blockquotes, task lists,
strikethrough, autolinks.

Two project-specific rules the build applies on top:

- Links to `something.md` are rewritten to `something.html`, so chapters can link to
  each other and stay correct both on GitHub and in the built site.
- Table header cells get `scope="col"` for screen readers.

## The generated pages

Each page is standalone — the CSS is inlined by pandoc, so there are no external
files or network requests, and `UserGuide/html/index.html` opens correctly straight
off disk.

The output is built to be accessible, which matters for this project specifically:
`lang` set, a skip-to-content link, a labelled `<nav>` landmark with `aria-current`
on the current page, a `<main>` landmark, headings in document order, `scope="col"`
on every table header, visible focus outlines, and a light/dark theme that follows
the reader's Windows setting. There is also a print stylesheet that drops the
navigation.

To change how pages look, edit `userguide.css`. To change their structure, edit
`userguide-template.html`. Neither needs the Markdown to be touched.

## Linking the guide from the app

The pages are self-contained, so opening `UserGuide/html/index.html` in the default
browser is all that is needed. Nothing in the app depends on this folder yet.
