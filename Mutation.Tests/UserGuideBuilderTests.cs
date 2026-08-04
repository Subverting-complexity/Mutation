using Markdig;
using Mutation.UserGuideBuilder;

namespace Mutation.Tests;

/// <summary>
/// Covers the user guide converter in Documentation\UserGuideBuilder.
///
/// The rules worth pinning down are the guide-specific ones layered on top of
/// Markdig: chapter links repointed from .md to .html, table headers marked up
/// for screen readers, and the reading order of the sidebar.
/// </summary>
public class UserGuideBuilderTests
{
	private static readonly MarkdownPipeline Pipeline = MarkdownRenderer.CreatePipeline();

	// ---- Chapter link rewriting -------------------------------------------

	[Theory]
	[InlineData("settings.md", "settings.html")]
	[InlineData("settings.md#saving", "settings.html#saving")]
	[InlineData("screen-capture-and-ocr.md", "screen-capture-and-ocr.html")]
	[InlineData("SETTINGS.MD", "SETTINGS.html")]
	public void RewriteChapterLink_repoints_chapter_links_to_generated_pages(string input, string expected)
	{
		Assert.Equal(expected, MarkdownRenderer.RewriteChapterLink(input));
	}

	[Theory]
	[InlineData("https://pandoc.org/installing.html")]
	[InlineData("http://example.com/a.md")]
	[InlineData("mailto:someone@example.com")]
	public void RewriteChapterLink_leaves_absolute_urls_alone(string url)
	{
		// An external .md link belongs to someone else's site and must not be touched.
		Assert.Equal(url, MarkdownRenderer.RewriteChapterLink(url));
	}

	[Theory]
	[InlineData("#a-heading-on-this-page")]
	[InlineData("index.html")]
	[InlineData("notes.txt")]
	public void RewriteChapterLink_leaves_non_chapter_targets_alone(string url)
	{
		Assert.Equal(url, MarkdownRenderer.RewriteChapterLink(url));
	}

	[Fact]
	public void Render_rewrites_chapter_links_but_not_external_ones()
	{
		string html = MarkdownRenderer.Render(
			"See [Settings](settings.md) and [pandoc](https://pandoc.org).",
			Pipeline);

		Assert.Contains(@"href=""settings.html""", html, StringComparison.Ordinal);
		Assert.Contains(@"href=""https://pandoc.org""", html, StringComparison.Ordinal);
	}

	// ---- Tables ------------------------------------------------------------

	private const string TableMarkdown = """
		| Shortcut | What it does |
		|---|---|
		| Alt+Q | Mute every microphone |
		""";

	[Fact]
	public void Render_marks_table_headers_with_scope_for_screen_readers()
	{
		string html = MarkdownRenderer.Render(TableMarkdown, Pipeline);

		Assert.Contains(@"scope=""col""", html, StringComparison.Ordinal);
		// Body cells must not be scoped - only the header row describes columns.
		Assert.DoesNotContain(@"<td scope", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Render_wraps_tables_so_they_can_scroll_without_losing_semantics()
	{
		string html = MarkdownRenderer.Render(TableMarkdown, Pipeline);

		Assert.Contains(@"<div class=""table-wrap""><table>", html, StringComparison.Ordinal);
		Assert.Contains("</table>\n</div>", html, StringComparison.Ordinal);
	}

	[Fact]
	public void WrapTables_wraps_every_table_on_a_page()
	{
		string html = MarkdownRenderer.WrapTables("<table>a</table><p>x</p><table>b</table>");

		Assert.Equal(2, CountOccurrences(html, @"<div class=""table-wrap"">"));
		Assert.Equal(2, CountOccurrences(html, "</div>"));
	}

	[Fact]
	public void WrapTables_leaves_pages_without_tables_untouched()
	{
		const string html = "<p>Nothing tabular here.</p>";

		Assert.Equal(html, MarkdownRenderer.WrapTables(html));
	}

	// ---- GitHub Flavored Markdown -----------------------------------------

	[Fact]
	public void Render_supports_the_github_flavoured_syntax_authors_expect()
	{
		string html = MarkdownRenderer.Render(
			"""
			~~struck~~ and https://example.com

			- [x] done
			- [ ] not done
			""",
			Pipeline);

		Assert.Contains("<del>struck</del>", html, StringComparison.Ordinal);
		Assert.Contains(@"<a href=""https://example.com""", html, StringComparison.Ordinal);
		Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Render_keeps_emphasis_that_wraps_across_source_lines()
	{
		// The guide is hard-wrapped at about 80 columns, so bold and links
		// routinely straddle a line break.
		string html = MarkdownRenderer.Render("the **Voice &\nSpeech** card", Pipeline);

		Assert.Contains("<strong>", html, StringComparison.Ordinal);
		Assert.DoesNotContain("**", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Render_gives_headings_ids_so_they_can_be_linked_to()
	{
		string html = MarkdownRenderer.Render("## Changing a shortcut", Pipeline);

		Assert.Contains(@"id=""changing-a-shortcut""", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Render_escapes_html_in_the_source()
	{
		string html = MarkdownRenderer.Render("A <script>alert(1)</script> tag.", Pipeline);

		Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
	}

	// ---- Titles ------------------------------------------------------------

	[Fact]
	public void ReadTitle_takes_the_first_level_one_heading()
	{
		string? title = ChapterDiscovery.ReadTitle("# Getting started\n\n## Not this\n", Pipeline);

		Assert.Equal("Getting started", title);
	}

	[Fact]
	public void ReadTitle_flattens_formatting_so_nav_labels_stay_plain()
	{
		string? title = ChapterDiscovery.ReadTitle("# Using `Alt+Q` and **bold**\n", Pipeline);

		Assert.Equal("Using Alt+Q and bold", title);
	}

	[Fact]
	public void ReadTitle_ignores_hashes_inside_fenced_code_blocks()
	{
		// Parsing rather than pattern matching is what makes this work.
		string? title = ChapterDiscovery.ReadTitle(
			"""
			```bash
			# not a heading
			```

			# The real title
			""",
			Pipeline);

		Assert.Equal("The real title", title);
	}

	[Fact]
	public void ReadTitle_returns_null_when_a_chapter_has_no_heading()
	{
		Assert.Null(ChapterDiscovery.ReadTitle("Just a paragraph.", Pipeline));
	}

	// ---- Reading order -----------------------------------------------------

	[Fact]
	public void Sort_puts_known_chapters_in_reading_order()
	{
		GuideChapter[] shuffled =
		[
			Chapter("troubleshooting"),
			Chapter("index"),
			Chapter("dictation"),
			Chapter("getting-started"),
		];

		string[] order = [.. ChapterDiscovery.Sort(shuffled).Select(c => c.Slug)];

		Assert.Equal(["index", "getting-started", "dictation", "troubleshooting"], order);
	}

	[Fact]
	public void Sort_appends_unknown_chapters_alphabetically_so_new_files_still_build()
	{
		GuideChapter[] chapters =
		[
			Chapter("zebra"),
			Chapter("index"),
			Chapter("aardvark"),
		];

		string[] order = [.. ChapterDiscovery.Sort(chapters).Select(c => c.Slug)];

		Assert.Equal(["index", "aardvark", "zebra"], order);
	}

	// ---- Page assembly -----------------------------------------------------

	[Fact]
	public void BrowserTitle_does_not_repeat_the_guide_name_on_the_contents_page()
	{
		PageTemplate template = PageTemplate.Load("Mutation User Guide");

		Assert.Equal("The Mutation User Guide", template.BrowserTitle("The Mutation User Guide"));
		Assert.Equal("Dictation - Mutation User Guide", template.BrowserTitle("Dictation"));
	}

	[Fact]
	public void Render_marks_the_current_chapter_so_a_screen_reader_can_announce_it()
	{
		PageTemplate template = PageTemplate.Load("Mutation User Guide");
		GuideChapter dictation = Chapter("dictation");
		IReadOnlyList<GuideChapter> all = [Chapter("index"), dictation];

		string html = template.Render(dictation, "<p>Body</p>", all, DateTimeOffset.Now);

		Assert.Contains(@"<a href=""dictation.html"" aria-current=""page"">", html, StringComparison.Ordinal);
		Assert.Contains(@"<a href=""index.html"">", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Render_produces_a_self_contained_page_with_no_leftover_placeholders()
	{
		PageTemplate template = PageTemplate.Load("Mutation User Guide");
		GuideChapter chapter = Chapter("index");

		string html = template.Render(chapter, "<p>Body</p>", [chapter], DateTimeOffset.Now);

		Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
		// Styling is inlined, so a page works straight off disk with no network.
		Assert.Contains("<style>", html, StringComparison.Ordinal);
		Assert.DoesNotContain("<link rel=\"stylesheet\"", html, StringComparison.Ordinal);
		// The landmarks a screen reader user relies on.
		Assert.Contains(@"<a class=""skip"" href=""#content"">", html, StringComparison.Ordinal);
		Assert.Contains(@"<main id=""content""", html, StringComparison.Ordinal);
		Assert.Contains(@"aria-label=""User guide contents""", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Render_escapes_chapter_titles_in_the_navigation()
	{
		PageTemplate template = PageTemplate.Load("Mutation User Guide");
		GuideChapter chapter = new("x.md", "x", "x.html", "Tom & Jerry <b>");

		string html = template.Render(chapter, "<p>Body</p>", [chapter], DateTimeOffset.Now);

		Assert.Contains("Tom &amp; Jerry &lt;b&gt;", html, StringComparison.Ordinal);
	}

	private static GuideChapter Chapter(string slug) =>
		new($"{slug}.md", slug, $"{slug}.html", slug);

	private static int CountOccurrences(string haystack, string needle)
	{
		int count = 0;
		int index = 0;

		while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += needle.Length;
		}

		return count;
	}
}
