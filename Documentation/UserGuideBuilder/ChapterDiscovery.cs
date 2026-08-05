using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Mutation.UserGuideBuilder;

/// <summary>
/// Finds the guide's chapters, puts them in reading order, and reads each one's
/// title out of its first level-1 heading.
/// </summary>
public static class ChapterDiscovery
{
	/// <summary>
	/// The order chapters appear in the sidebar. Anything not listed here still
	/// builds - it is appended alphabetically - so adding a chapter never breaks
	/// the build, it just lands at the end until it is added to this list.
	/// </summary>
	public static readonly string[] ReadingOrder =
	[
		GuideChapter.ContentsSlug,
		"getting-started",
		"main-window",
		"microphone",
		"dictation",
		"screen-capture-and-ocr",
		"read-aloud",
		"ai-prompts",
		"transcript-formatting",
		"keyboard-shortcuts",
		"settings",
		"accessibility",
		"troubleshooting",
	];

	/// <summary>
	/// Reads every chapter in <paramref name="markdownDirectory"/>, skipping files
	/// whose name starts with an underscore, and returns them in reading order.
	/// </summary>
	public static IReadOnlyList<GuideChapter> Discover(string markdownDirectory, MarkdownPipeline pipeline)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(markdownDirectory);
		ArgumentNullException.ThrowIfNull(pipeline);

		if (!Directory.Exists(markdownDirectory))
		{
			throw new DirectoryNotFoundException($"Markdown folder not found: {markdownDirectory}");
		}

		List<GuideChapter> chapters = [];

		foreach (string path in Directory.EnumerateFiles(markdownDirectory, "*.md"))
		{
			string slug = Path.GetFileNameWithoutExtension(path);
			if (slug.StartsWith('_'))
			{
				continue;
			}

			string markdown = File.ReadAllText(path);
			chapters.Add(new GuideChapter(
				SourcePath: path,
				Slug: slug,
				OutputFileName: slug + ".html",
				Title: ReadTitle(markdown, pipeline) ?? slug));
		}

		if (chapters.Count == 0)
		{
			throw new InvalidOperationException($"No Markdown files found in {markdownDirectory}");
		}

		return Sort(chapters);
	}

	/// <summary>
	/// Puts known chapters in <see cref="ReadingOrder"/> first, then anything else
	/// alphabetically.
	/// </summary>
	public static IReadOnlyList<GuideChapter> Sort(IEnumerable<GuideChapter> chapters)
	{
		ArgumentNullException.ThrowIfNull(chapters);

		List<GuideChapter> remaining = [.. chapters];
		List<GuideChapter> ordered = [];

		foreach (string slug in ReadingOrder)
		{
			GuideChapter? match = remaining.FirstOrDefault(c =>
				string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));

			if (match is not null)
			{
				ordered.Add(match);
				remaining.Remove(match);
			}
		}

		ordered.AddRange(remaining.OrderBy(c => c.Slug, StringComparer.OrdinalIgnoreCase));
		return ordered;
	}

	/// <summary>
	/// Returns the text of the first level-1 heading, or null if there isn't one.
	/// Parsing rather than pattern matching means a '#' inside a fenced code block
	/// is never mistaken for the title.
	/// </summary>
	public static string? ReadTitle(string markdown, MarkdownPipeline pipeline)
	{
		ArgumentNullException.ThrowIfNull(markdown);
		ArgumentNullException.ThrowIfNull(pipeline);

		MarkdownDocument document = Markdown.Parse(markdown, pipeline);

		HeadingBlock? heading = document
			.Descendants<HeadingBlock>()
			.FirstOrDefault(h => h.Level == 1);

		if (heading is null)
		{
			return null;
		}

		string title = PlainText(heading);
		return string.IsNullOrWhiteSpace(title) ? null : title;
	}

	/// <summary>
	/// Flattens a heading's inline content to plain text, so "Fixing **bold** text"
	/// becomes "Fixing bold text" for use in a title tag or a nav label.
	/// </summary>
	private static string PlainText(LeafBlock block)
	{
		if (block.Inline is null)
		{
			return string.Empty;
		}

		StringBuilder text = new();

		foreach (Inline inline in block.Inline.Descendants())
		{
			switch (inline)
			{
				case LiteralInline literal:
					text.Append(literal.Content.AsSpan());
					break;
				case CodeInline code:
					text.Append(code.Content);
					break;
				default:
					break;
			}
		}

		return text.ToString().Trim();
	}
}
