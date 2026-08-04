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
