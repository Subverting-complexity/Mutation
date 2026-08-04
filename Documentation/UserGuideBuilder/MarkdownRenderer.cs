using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Mutation.UserGuideBuilder;

/// <summary>
/// Turns a chapter's Markdown into the HTML that goes inside &lt;main&gt;.
///
/// Markdig does the parsing, so the guide can use anything GitHub Flavored
/// Markdown supports. Two guide-specific rules are applied on top: chapter links
/// are repointed from .md to .html, and tables are made screen-reader friendly.
/// </summary>
public static partial class MarkdownRenderer
{
	/// <summary>
	/// The Markdown dialect the guide is written in: CommonMark plus the GitHub
	/// extensions authors actually reach for, so what renders on GitHub renders
	/// here too.
	///
	/// Raw HTML is escaped rather than passed through. The page shell carries the
	/// landmarks a screen reader navigates by - the skip target, the nav, the
	/// main region - and a stray unclosed tag in a chapter could silently break
	/// that nesting for every reader of that page. No chapter needs raw HTML, so
	/// the safer default wins.
	/// </summary>
	public static MarkdownPipeline CreatePipeline() =>
		new MarkdownPipelineBuilder()
			.UsePipeTables()
			.UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
			.UseAutoLinks()
			.UseTaskLists()
			.UseEmphasisExtras()
			.DisableHtml()
			.Build();

	/// <summary>
	/// Renders one chapter to the HTML fragment that goes inside &lt;main&gt;.
	/// </summary>
	public static string Render(string markdown, MarkdownPipeline pipeline)
	{
		ArgumentNullException.ThrowIfNull(markdown);
		ArgumentNullException.ThrowIfNull(pipeline);

		MarkdownDocument document = Markdown.Parse(markdown, pipeline);

		RewriteChapterLinks(document);
		MarkTableHeaderScope(document);

		using StringWriter writer = new();
		HtmlRenderer renderer = new(writer);
		pipeline.Setup(renderer);
		renderer.Render(document);
		writer.Flush();

		return WrapTables(writer.ToString());
	}

	/// <summary>
	/// Repoints links like [Settings](settings.md) at settings.html.
	///
	/// The Markdown keeps linking to .md so the chapters stay correct when read on
	/// their own or browsed on GitHub; only the generated site sees .html.
	/// </summary>
	public static void RewriteChapterLinks(MarkdownDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);

		foreach (LinkInline link in document.Descendants<LinkInline>())
		{
			if (link.Url is { Length: > 0 } url)
			{
				link.Url = RewriteChapterLink(url);
			}
		}
	}

	/// <summary>
	/// Rewrites a single link target. Absolute URLs are left exactly as they are.
	/// </summary>
	public static string RewriteChapterLink(string url)
	{
		ArgumentNullException.ThrowIfNull(url);

		// Anything with a scheme - https:, mailto: - points off the site.
		if (AbsoluteUrl().IsMatch(url))
		{
			return url;
		}

		int anchorAt = url.IndexOf('#', StringComparison.Ordinal);
		string path = anchorAt >= 0 ? url[..anchorAt] : url;
		string anchor = anchorAt >= 0 ? url[anchorAt..] : string.Empty;

		if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
		{
			return url;
		}

		return string.Concat(path.AsSpan(0, path.Length - ".md".Length), ".html", anchor);
	}

	/// <summary>
	/// Adds scope="col" to every table header cell. Markdig does not emit it, and
	/// without it a screen reader cannot say which column a value belongs to.
	/// Every table in this guide is column-headed, so this is safe across the set.
	/// </summary>
	public static void MarkTableHeaderScope(MarkdownDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);

		foreach (Table table in document.Descendants<Table>())
		{
			foreach (TableRow row in table.OfType<TableRow>().Where(r => r.IsHeader))
			{
				foreach (TableCell cell in row.OfType<TableCell>())
				{
					cell.GetAttributes().AddProperty("scope", "col");
				}
			}
		}
	}

	/// <summary>
	/// Wraps each table in a scrollable div.
	///
	/// This is an accessibility fix, not a cosmetic one. A wide table has to scroll
	/// sideways in a narrow window, but doing that with `table { display: block }`
	/// costs the element its implicit table semantics in several browser and
	/// screen-reader combinations, so rows and columns stop being announced as a
	/// table. Scrolling the wrapper instead leaves the table a table.
	///
	/// Done on the rendered HTML because a wrapper is not a Markdown construct and
	/// so has no place in the document tree. Markdown has no way to nest tables,
	/// so a flat open/close replacement is sound here.
	/// </summary>
	public static string WrapTables(string html)
	{
		ArgumentNullException.ThrowIfNull(html);

		html = TableOpen().Replace(html, @"<div class=""table-wrap"">$0");
		html = TableClose().Replace(html, "$0\n</div>");
		return html;
	}

	[GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.-]*:")]
	private static partial Regex AbsoluteUrl();

	[GeneratedRegex(@"<table(?:\s[^>]*)?>")]
	private static partial Regex TableOpen();

	[GeneratedRegex(@"</table>")]
	private static partial Regex TableClose();
}
