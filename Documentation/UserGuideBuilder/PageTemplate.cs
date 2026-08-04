using System.Net;
using System.Reflection;
using System.Text;

namespace Mutation.UserGuideBuilder;

/// <summary>
/// Assembles a finished page: the shell from Assets\userguide-template.html, the
/// stylesheet inlined, the sidebar, the chapter body and the footer.
/// </summary>
public sealed class PageTemplate
{
	// Default resource names are <RootNamespace>.<folder>.<file>.
	private const string TemplateResource = "Mutation.UserGuideBuilder.Assets.userguide-template.html";
	private const string CssResource = "Mutation.UserGuideBuilder.Assets.userguide.css";

	private readonly string _shell;
	private readonly string _css;
	private readonly string _siteTitle;

	private PageTemplate(string shell, string css, string siteTitle)
	{
		_shell = shell;
		_css = css;
		_siteTitle = siteTitle;
	}

	/// <summary>
	/// Loads the shell and stylesheet that were embedded at build time, so the tool
	/// needs no files beside it and does not care what the working directory is.
	/// </summary>
	public static PageTemplate Load(string siteTitle)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(siteTitle);

		return new PageTemplate(
			StripLeadingComment(ReadResource(TemplateResource)),
			ReadResource(CssResource),
			siteTitle);
	}

	/// <summary>
	/// Drops the maintainer note at the top of the template. It explains the
	/// tokens to whoever edits the file, and has no business being copied into
	/// every published page.
	/// </summary>
	internal static string StripLeadingComment(string template)
	{
		ArgumentNullException.ThrowIfNull(template);

		string trimmed = template.TrimStart();
		if (!trimmed.StartsWith("<!--", StringComparison.Ordinal))
		{
			return template;
		}

		int end = trimmed.IndexOf("-->", StringComparison.Ordinal);
		return end < 0 ? template : trimmed[(end + "-->".Length)..].TrimStart();
	}

	/// <summary>
	/// Builds the complete HTML for one chapter. <paramref name="allChapters"/> is
	/// every chapter in reading order, used to render the sidebar.
	/// </summary>
	public string Render(GuideChapter chapter, string bodyHtml, IReadOnlyList<GuideChapter> allChapters, DateTimeOffset builtOn)
	{
		ArgumentNullException.ThrowIfNull(chapter);
		ArgumentNullException.ThrowIfNull(bodyHtml);
		ArgumentNullException.ThrowIfNull(allChapters);

		string page = _shell
			.Replace("{{TITLE}}", WebUtility.HtmlEncode(BrowserTitle(chapter.Title)), StringComparison.Ordinal)
			.Replace("{{CSS}}", _css, StringComparison.Ordinal)
			.Replace("{{NAV}}", BuildNav(chapter, allChapters), StringComparison.Ordinal)
			.Replace("{{BODY}}", bodyHtml, StringComparison.Ordinal)
			.Replace("{{FOOTER}}", BuildFooter(builtOn, chapter.IsContentsPage), StringComparison.Ordinal);

		return NormaliseLineEndings(page);
	}

	/// <summary>
	/// Forces LF endings on the generated page.
	///
	/// These pages are committed, and .gitattributes puts the whole repository on
	/// LF. Without this the output picks up CRLF from Environment.NewLine, and
	/// every rebuild marks all 13 pages as modified even when nothing changed -
	/// noise that hides the edits that do matter.
	/// </summary>
	internal static string NormaliseLineEndings(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		return text.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace('\r', '\n');
	}

	/// <summary>
	/// The browser tab title. The contents page is already named after the guide,
	/// so it is not suffixed with the guide name a second time.
	/// </summary>
	public string BrowserTitle(string chapterTitle)
	{
		ArgumentNullException.ThrowIfNull(chapterTitle);

		return chapterTitle.Contains(_siteTitle, StringComparison.OrdinalIgnoreCase)
			? chapterTitle
			: $"{chapterTitle} - {_siteTitle}";
	}

	/// <summary>
	/// The sidebar. It is rebuilt for each page so the current chapter can carry
	/// aria-current, which is what tells a screen reader where it is in the guide.
	/// </summary>
	private string BuildNav(GuideChapter current, IReadOnlyList<GuideChapter> allChapters)
	{
		StringBuilder nav = new();
		nav.AppendLine(@"<nav class=""sidebar"" aria-label=""User guide contents"">");
		nav.AppendLine($"<h2>{WebUtility.HtmlEncode(_siteTitle)}</h2>");
		nav.AppendLine("<ol>");

		foreach (GuideChapter chapter in allChapters)
		{
			string ariaCurrent = chapter.OutputFileName == current.OutputFileName
				? @" aria-current=""page"""
				: string.Empty;

			nav.AppendLine(
				$@"<li><a href=""{chapter.OutputFileName}""{ariaCurrent}>{WebUtility.HtmlEncode(chapter.Title)}</a></li>");
		}

		nav.AppendLine("</ol>");
		nav.AppendLine("</nav>");
		return nav.ToString();
	}

	/// <summary>
	/// The page footer. Only the contents page is dated.
	///
	/// These pages are committed, so a footer date on every page means a rebuild on a
	/// new day rewrites all 13 of them even when no wording changed — noise that hides
	/// the edits that matter and collides between concurrent branches. The date is
	/// genuinely useful (it says how current the guide is), so it stays, on the one page
	/// a reader arrives at. Same reasoning as NormaliseLineEndings above.
	/// </summary>
	private static string BuildFooter(DateTimeOffset builtOn, bool isContentsPage)
	{
		const string Provenance =
			"The Markdown files are the source of truth &ndash; do not edit these HTML pages by hand.";

		string note = isContentsPage
			? $"Generated from the Markdown source on {builtOn:d MMMM yyyy}. {Provenance}"
			: Provenance;

		return $"""
			<footer>
			<p><a href="index.html">Back to the contents page</a></p>
			<p>{note}</p>
			</footer>
			""";
	}

	private static string ReadResource(string name)
	{
		Assembly assembly = typeof(PageTemplate).Assembly;
		using Stream? stream = assembly.GetManifestResourceStream(name);

		if (stream is null)
		{
			throw new InvalidOperationException(
				$"Embedded resource '{name}' is missing. Check the EmbeddedResource entries in UserGuideBuilder.csproj.");
		}

		using StreamReader reader = new(stream);
		return reader.ReadToEnd();
	}
}
