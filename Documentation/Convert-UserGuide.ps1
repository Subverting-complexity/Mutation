<#
	Convert-UserGuide.ps1

	Builds the HTML version of the Mutation user guide from the Markdown files
	in .\UserGuide\markdown, writing the result into .\UserGuide\html.

	The Markdown files are the source of truth. The HTML is generated output —
	never edit the HTML by hand; edit the Markdown and re-run Build-UserGuide.cmd.

	Conversion is done by pandoc. Supporting files, all in this folder:

		userguide-template.html  page shell (skip link, nav slot, main landmark)
		userguide.css            styling, inlined into each page at build time
		userguide.lua            rewrites .md links to .html, adds table scope

	pandoc is looked for in this folder first, then on PATH.
#>

[CmdletBinding()]
param(
	[string] $MarkdownPath,
	[string] $HtmlPath,
	[string] $Pandoc,
	[string] $SiteTitle = 'Mutation User Guide'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $MarkdownPath) { $MarkdownPath = Join-Path $root 'UserGuide\markdown' }
if (-not $HtmlPath) { $HtmlPath = Join-Path $root 'UserGuide\html' }

$template = Join-Path $root 'userguide-template.html'
$css = Join-Path $root 'userguide.css'
$filter = Join-Path $root 'userguide.lua'

# The order chapters appear in the sidebar. Anything not listed is appended
# alphabetically, so adding a new chapter never breaks the build.
$chapterOrder = @(
	'index',
	'getting-started',
	'main-window',
	'microphone',
	'dictation',
	'screen-capture-and-ocr',
	'read-aloud',
	'ai-prompts',
	'transcript-formatting',
	'keyboard-shortcuts',
	'settings',
	'accessibility',
	'troubleshooting'
)

# ----------------------------------------------------------------------------
# Locate pandoc
# ----------------------------------------------------------------------------

function Resolve-Pandoc([string] $explicit, [string] $scriptRoot) {
	if ($explicit) {
		if (Test-Path $explicit) { return (Resolve-Path $explicit).Path }
		throw "pandoc was not found at the path you gave: $explicit"
	}

	$local = Join-Path $scriptRoot 'pandoc.exe'
	if (Test-Path $local) { return $local }

	$onPath = Get-Command 'pandoc' -ErrorAction SilentlyContinue
	if ($onPath) { return $onPath.Source }

	throw @"
pandoc was not found.

Put pandoc.exe in this folder:
    $scriptRoot

or install it so that 'pandoc' works from a command prompt, or run this script
with -Pandoc "C:\path\to\pandoc.exe".

Download: https://pandoc.org/installing.html
"@
}

# Setup problems are the ones a non-technical user is most likely to hit, so
# report them as a plain sentence rather than a PowerShell stack trace.
try {
	$pandocExe = Resolve-Pandoc $Pandoc $root

	foreach ($required in @($template, $css, $filter)) {
		if (-not (Test-Path $required)) {
			throw "A supporting file is missing: $required"
		}
	}
	if (-not (Test-Path $MarkdownPath)) {
		throw "Markdown folder not found: $MarkdownPath"
	}
	if (-not (Test-Path $HtmlPath)) {
		New-Item -ItemType Directory -Path $HtmlPath | Out-Null
	}
}
catch {
	Write-Host ''
	Write-Host $_.Exception.Message -ForegroundColor Red
	Write-Host ''
	exit 1
}

Write-Host "Using pandoc: $pandocExe"
Write-Host ''

# ----------------------------------------------------------------------------
# Work out the chapter list and each chapter's title
# ----------------------------------------------------------------------------

$files = Get-ChildItem -Path $MarkdownPath -Filter '*.md' -File |
	Where-Object { $_.Name -notlike '_*' }

if (-not $files) { throw "No Markdown files found in $MarkdownPath" }

$ordered = @()
foreach ($name in $chapterOrder) {
	$match = $files | Where-Object { $_.BaseName -eq $name }
	if ($match) { $ordered += $match }
}
$ordered += ($files | Where-Object { $chapterOrder -notcontains $_.BaseName } | Sort-Object Name)

# A chapter's title is its first level-1 heading, which every chapter starts with.
$pages = foreach ($file in $ordered) {
	$firstHeading = Select-String -Path $file.FullName -Pattern '^#\s+(.+?)\s*$' |
		Select-Object -First 1

	$title = if ($firstHeading) {
		($firstHeading.Matches[0].Groups[1].Value -replace '\*\*', '' -replace '`', '').Trim()
	}
	else { $file.BaseName }

	[pscustomobject]@{
		Source = $file
		File   = "$($file.BaseName).html"
		Title  = $title
	}
}

# ----------------------------------------------------------------------------
# Build
# ----------------------------------------------------------------------------

# Clear out stale HTML so a renamed or deleted chapter leaves no orphan behind.
Get-ChildItem -Path $HtmlPath -Filter '*.html' -File -ErrorAction SilentlyContinue |
	Where-Object { $pages.File -notcontains $_.Name } |
	ForEach-Object {
		Write-Host "  removed stale $($_.Name)"
		Remove-Item $_.FullName -Force
	}

$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("mutation-guide-" + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $scratch | Out-Null

$stamp = (Get-Date).ToString('d MMMM yyyy')
$failed = 0

try {
	foreach ($page in $pages) {
		# The sidebar is rebuilt per page so the current chapter can carry
		# aria-current, which is what tells a screen reader where it is.
		$nav = New-Object System.Text.StringBuilder
		$nav.AppendLine('<nav class="sidebar" aria-label="User guide contents">') | Out-Null
		$nav.AppendLine("<h2>$([System.Net.WebUtility]::HtmlEncode($SiteTitle))</h2>") | Out-Null
		$nav.AppendLine('<ol>') | Out-Null
		foreach ($item in $pages) {
			$current = if ($item.File -eq $page.File) { ' aria-current="page"' } else { '' }
			$label = [System.Net.WebUtility]::HtmlEncode($item.Title)
			$nav.AppendLine("<li><a href=""$($item.File)""$current>$label</a></li>") | Out-Null
		}
		$nav.AppendLine('</ol>') | Out-Null
		$nav.AppendLine('</nav>') | Out-Null

		$footer = @"
<footer>
<p><a href="index.html">Back to the contents page</a></p>
<p>Generated from the Markdown source on $stamp. The Markdown files are the source of truth &ndash; do not edit these HTML pages by hand.</p>
</footer>
"@

		$navFile = Join-Path $scratch 'nav.html'
		$footFile = Join-Path $scratch 'footer.html'
		$utf8 = New-Object System.Text.UTF8Encoding $false
		[System.IO.File]::WriteAllText($navFile, $nav.ToString(), $utf8)
		[System.IO.File]::WriteAllText($footFile, $footer, $utf8)

		# The contents page is already named after the guide; don't say it twice.
		$headTitle = if ($page.Title -like "*$SiteTitle*") { $page.Title } else { "$($page.Title) - $SiteTitle" }

		$target = Join-Path $HtmlPath $page.File

		$pandocArgs = @(
			'--from=gfm'
			'--to=html5'
			'--standalone'
			'--embed-resources'
			"--template=$template"
			"--css=$css"
			"--lua-filter=$filter"
			"--include-before-body=$navFile"
			"--include-after-body=$footFile"
			"--metadata=pagetitle:$headTitle"
			'--metadata=lang:en'
			'--output'
			$target
			$page.Source.FullName
		)

		& $pandocExe @pandocArgs
		if ($LASTEXITCODE -ne 0) {
			Write-Warning "pandoc failed on $($page.Source.Name) (exit $LASTEXITCODE)"
			$failed++
			continue
		}

		Write-Host "  $($page.Source.Name) -> $($page.File)"
	}
}
finally {
	Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
if ($failed -gt 0) {
	Write-Host "$failed page(s) FAILED to build." -ForegroundColor Red
	exit 1
}

Write-Host "Done. $($pages.Count) page(s) written to $HtmlPath"
Write-Host "Open $(Join-Path $HtmlPath 'index.html') to read the guide."
