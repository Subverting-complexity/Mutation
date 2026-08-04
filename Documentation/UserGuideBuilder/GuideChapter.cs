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
	string Title);
