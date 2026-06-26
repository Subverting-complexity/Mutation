using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// Verifies the per-rule speech preprocessing: each cleanup rule must apply only
// when its toggle is on, the all-on result must match the original behaviour, and
// the settings-to-options mapping must carry each flag faithfully.
public class SpeechPreprocessingTests
{
	// Build options with every rule off except the ones named, so each test
	// exercises a single rule in isolation.
	private static SpeechPreprocessingOptions With(
		bool code = false, bool emph = false, bool head = false, bool link = false,
		bool bullet = false, bool abbr = false, bool ws = false)
		=> new()
		{
			RemoveCodeBlocks = code,
			StripBoldItalicCode = emph,
			StripHeadingMarks = head,
			ShortenWebLinks = link,
			StripBulletMarkers = bullet,
			ExpandAbbreviations = abbr,
			NormaliseWhitespace = ws,
		};

	[Fact]
	public void Empty_ReturnsInput()
	{
		Assert.Equal(string.Empty, TextToSpeechService.PreprocessForSpeech(string.Empty, SpeechPreprocessingOptions.All));
	}

	[Fact]
	public void NullOptions_TreatedAsAllRules()
	{
		const string input = "# Heading\n\n**bold** see https://example.com now e.g. here";
		Assert.Equal(
			TextToSpeechService.PreprocessForSpeech(input, SpeechPreprocessingOptions.All),
			TextToSpeechService.PreprocessForSpeech(input, null!));
	}

	[Fact]
	public void DefaultOverload_MatchesAllRulesOn()
	{
		const string input = "# Title\n\n- a bullet with **bold**, `code`, e.g. an example, https://example.com/x ```block```";
		Assert.Equal(
			TextToSpeechService.PreprocessForSpeech(input),
			TextToSpeechService.PreprocessForSpeech(input, SpeechPreprocessingOptions.All));
	}

	[Fact]
	public void AllOff_LeavesTextUnchangedApartFromTrim()
	{
		const string input = "# Title with **bold** and `code`";
		Assert.Equal(input, TextToSpeechService.PreprocessForSpeech(input, With()));
	}

	[Fact]
	public void RemoveCodeBlocks_Toggle()
	{
		const string input = "before ```secret code``` after";
		Assert.DoesNotContain("secret code", TextToSpeechService.PreprocessForSpeech(input, With(code: true)));
		Assert.Contains("secret code", TextToSpeechService.PreprocessForSpeech(input, With(code: false)));
	}

	[Fact]
	public void StripBoldItalicCode_Toggle()
	{
		const string input = "x **bold** _ital_ `code` y";
		string on = TextToSpeechService.PreprocessForSpeech(input, With(emph: true));
		Assert.DoesNotContain("**", on);
		Assert.DoesNotContain("`", on);
		Assert.Contains("bold", on);
		Assert.Contains("**", TextToSpeechService.PreprocessForSpeech(input, With(emph: false)));
	}

	[Fact]
	public void StripHeadingMarks_Toggle()
	{
		const string input = "## Heading text";
		Assert.Equal("Heading text", TextToSpeechService.PreprocessForSpeech(input, With(head: true)));
		Assert.Contains("##", TextToSpeechService.PreprocessForSpeech(input, With(head: false)));
	}

	[Fact]
	public void ShortenWebLinks_Toggle()
	{
		const string input = "see https://example.com/page now";
		Assert.Contains("link to example.com", TextToSpeechService.PreprocessForSpeech(input, With(link: true)));
		Assert.Contains("https://example.com", TextToSpeechService.PreprocessForSpeech(input, With(link: false)));
	}

	[Fact]
	public void StripBulletMarkers_Toggle()
	{
		const string input = "- item one";
		Assert.Equal("item one", TextToSpeechService.PreprocessForSpeech(input, With(bullet: true)));
		Assert.Contains("- item one", TextToSpeechService.PreprocessForSpeech(input, With(bullet: false)));
	}

	[Fact]
	public void ExpandAbbreviations_Toggle()
	{
		const string input = "e.g. this, i.e. that, etc. vs. them";
		string on = TextToSpeechService.PreprocessForSpeech(input, With(abbr: true));
		Assert.Contains("for example", on);
		Assert.Contains("that is", on);
		Assert.Contains("et cetera", on);
		Assert.Contains("versus", on);
		Assert.Contains("e.g.", TextToSpeechService.PreprocessForSpeech(input, With(abbr: false)));
	}

	[Fact]
	public void NormaliseWhitespace_Toggle()
	{
		const string input = "a\n\nb   c";
		Assert.Equal("a. b c", TextToSpeechService.PreprocessForSpeech(input, With(ws: true)));
		Assert.Contains("\n", TextToSpeechService.PreprocessForSpeech(input, With(ws: false)));
	}

	[Fact]
	public void FromSettings_MapsEachFlag()
	{
		var settings = new TextToSpeechSettings
		{
			PreprocessRemoveCodeBlocks = false,
			PreprocessStripBoldItalicCode = true,
			PreprocessStripHeadingMarks = false,
			PreprocessShortenWebLinks = true,
			PreprocessStripBulletMarkers = false,
			PreprocessExpandAbbreviations = true,
			PreprocessNormaliseWhitespace = false,
		};

		var options = SpeechPreprocessingOptions.FromSettings(settings);

		Assert.False(options.RemoveCodeBlocks);
		Assert.True(options.StripBoldItalicCode);
		Assert.False(options.StripHeadingMarks);
		Assert.True(options.ShortenWebLinks);
		Assert.False(options.StripBulletMarkers);
		Assert.True(options.ExpandAbbreviations);
		Assert.False(options.NormaliseWhitespace);
	}

	[Fact]
	public void FromSettings_DefaultsAreAllOn()
	{
		var options = SpeechPreprocessingOptions.FromSettings(new TextToSpeechSettings());

		Assert.True(options.RemoveCodeBlocks);
		Assert.True(options.StripBoldItalicCode);
		Assert.True(options.StripHeadingMarks);
		Assert.True(options.ShortenWebLinks);
		Assert.True(options.StripBulletMarkers);
		Assert.True(options.ExpandAbbreviations);
		Assert.True(options.NormaliseWhitespace);
	}
}
