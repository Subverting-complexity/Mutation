namespace CognitiveSupport;

// Per-rule switches for speech preprocessing. Each property gates one cleanup
// rule applied to text before it is spoken. Every rule defaults to on, so the
// combined result matches the previous all-or-nothing cleanup unless a user
// turns an individual rule off. The master EnableSpeechPreprocessing switch
// gates whether any of these rules run at all.
public sealed class SpeechPreprocessingOptions
{
	// Drop fenced ``` code blocks ``` entirely.
	public bool RemoveCodeBlocks { get; init; } = true;

	// Strip bold (**), italic (* or _), and inline-code (`) symbols, keeping the
	// inner text.
	public bool StripBoldItalicCode { get; init; } = true;

	// Strip leading heading marks (#, ##, ...).
	public bool StripHeadingMarks { get; init; } = true;

	// Replace web links with a short "link to {host}" phrase.
	public bool ShortenWebLinks { get; init; } = true;

	// Strip leading bullet markers (-, *, +).
	public bool StripBulletMarkers { get; init; } = true;

	// Expand common abbreviations (e.g., i.e., etc., vs.).
	public bool ExpandAbbreviations { get; init; } = true;

	// Collapse paragraph breaks and runs of whitespace into single spaces.
	public bool NormaliseWhitespace { get; init; } = true;

	// Every rule enabled — the default cleanup behaviour, applied when no
	// per-rule options are supplied.
	public static SpeechPreprocessingOptions All { get; } = new();

	// Map the persisted per-rule settings onto an options instance.
	public static SpeechPreprocessingOptions FromSettings(TextToSpeechSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		return new SpeechPreprocessingOptions
		{
			RemoveCodeBlocks = settings.PreprocessRemoveCodeBlocks,
			StripBoldItalicCode = settings.PreprocessStripBoldItalicCode,
			StripHeadingMarks = settings.PreprocessStripHeadingMarks,
			ShortenWebLinks = settings.PreprocessShortenWebLinks,
			StripBulletMarkers = settings.PreprocessStripBulletMarkers,
			ExpandAbbreviations = settings.PreprocessExpandAbbreviations,
			NormaliseWhitespace = settings.PreprocessNormaliseWhitespace,
		};
	}
}
