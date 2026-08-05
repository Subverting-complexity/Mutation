using Markdig;

namespace Mutation.UserGuideBuilder;

/// <summary>
/// Builds every chapter into <see cref="BuildResult"/>, writing one HTML page per
/// Markdown file and clearing out pages whose chapter no longer exists.
/// </summary>
public static class GuideBuilder
{
	/// <summary>What a build produced, for reporting back to the caller.</summary>
	/// <param name="PagesWritten">Generated page names, in reading order.</param>
	/// <param name="StalePagesRemoved">Pages deleted because their chapter is gone.</param>
	public sealed record BuildResult(
		IReadOnlyList<string> PagesWritten,
		IReadOnlyList<string> StalePagesRemoved);

	/// <summary>
	/// Renders <paramref name="markdownDirectory"/> into <paramref name="htmlDirectory"/>.
	/// </summary>
	public static BuildResult Build(string markdownDirectory, string htmlDirectory, string siteTitle, DateTimeOffset builtOn)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(markdownDirectory);
		ArgumentException.ThrowIfNullOrWhiteSpace(htmlDirectory);

		MarkdownPipeline pipeline = MarkdownRenderer.CreatePipeline();
		IReadOnlyList<GuideChapter> chapters = ChapterDiscovery.Discover(markdownDirectory, pipeline);

		// Every page's footer links back to the contents page, and it is the one page
		// that carries the build date. Without it the build would quietly produce a set
		// of pages with a dead "back to the contents page" link on each of them and no
		// date anywhere.
		if (!chapters.Any(c => c.IsContentsPage))
		{
			throw new InvalidOperationException(
				$"No contents page found: {markdownDirectory} has no {GuideChapter.ContentsSlug}.md. " +
				"Every other page links back to it, so the guide cannot be built without one.");
		}

		PageTemplate template = PageTemplate.Load(siteTitle);

		Directory.CreateDirectory(htmlDirectory);

		List<string> removed = RemoveStalePages(htmlDirectory, chapters);
		List<string> written = [];

		foreach (GuideChapter chapter in chapters)
		{
			string markdown = File.ReadAllText(chapter.SourcePath);
			string body = MarkdownRenderer.Render(markdown, pipeline);
			string page = template.Render(chapter, body, chapters, builtOn);

			File.WriteAllText(Path.Combine(htmlDirectory, chapter.OutputFileName), page);
			written.Add(chapter.OutputFileName);
		}

		return new BuildResult(written, removed);
	}

	/// <summary>
	/// Deletes generated pages that no longer have a chapter behind them, so
	/// renaming or removing a chapter cannot leave an orphan page linked from
	/// nowhere.
	/// </summary>
	private static List<string> RemoveStalePages(string htmlDirectory, IReadOnlyList<GuideChapter> chapters)
	{
		HashSet<string> expected = chapters
			.Select(c => c.OutputFileName)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		List<string> removed = [];

		foreach (string path in Directory.EnumerateFiles(htmlDirectory, "*.html"))
		{
			string name = Path.GetFileName(path);
			if (!expected.Contains(name))
			{
				File.Delete(path);
				removed.Add(name);
			}
		}

		return removed;
	}
}
