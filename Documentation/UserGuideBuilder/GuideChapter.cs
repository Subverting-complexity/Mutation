namespace Mutation.UserGuideBuilder;

/// <summary>
/// One chapter of the user guide: where its Markdown lives, what the generated
/// page is called, and the title shown in the sidebar and the browser tab.
/// </summary>
/// <param name="SourcePath">Full path to the chapter's .md file.</param>
/// <param name="Slug">File name without extension, e.g. "getting-started".</param>
/// <param name="OutputFileName">Generated page name, e.g. "getting-started.html".</param>
/// <param name="Title">The chapter's first level-1 heading.</param>
public sealed record GuideChapter(
	string SourcePath,
	string Slug,
	string OutputFileName,
	string Title)
{
	/// <summary>
	/// The guide's contents page. It is the page every other page links back to, and
	/// the one page that carries the build date - so a rebuild on a new day touches one
	/// file rather than all of them.
	/// </summary>
	public const string ContentsSlug = "index";

	/// <summary>The generated contents page, i.e. what "back to contents" points at.</summary>
	public const string ContentsFileName = ContentsSlug + ".html";

	/// <summary>Whether this is the guide's contents page.</summary>
	public bool IsContentsPage =>
		string.Equals(Slug, ContentsSlug, StringComparison.OrdinalIgnoreCase);
}
