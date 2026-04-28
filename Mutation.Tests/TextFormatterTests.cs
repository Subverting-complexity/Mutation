using CognitiveSupport;
using static CognitiveSupport.LlmSettings;
using static CognitiveSupport.LlmSettings.TranscriptFormatRule;

namespace Mutation.Tests;

public class TextFormatterTests
{
	private static TranscriptFormatRule Rule(string find, string replace, MatchTypeEnum matchType, bool caseSensitive = false)
		=> new TranscriptFormatRule(find, replace, caseSensitive, matchType);

	// ----- FormatWithRule: Plain -----

	[Fact]
	public void FormatWithRule_Plain_CaseInsensitive_ReplacesAllOccurrences()
	{
		string result = TextFormatter.FormatWithRule(
			"Hello hello HELLO",
			Rule("hello", "world", MatchTypeEnum.Plain));

		Assert.Equal("world world world", result);
	}

	[Fact]
	public void FormatWithRule_Plain_CaseSensitive_OnlyReplacesExactCase()
	{
		string result = TextFormatter.FormatWithRule(
			"Hello hello HELLO",
			Rule("hello", "world", MatchTypeEnum.Plain, caseSensitive: true));

		Assert.Equal("Hello world HELLO", result);
	}

	// ----- FormatWithRule: RegEx -----

	[Fact]
	public void FormatWithRule_RegEx_CaseInsensitive_AppliesPattern()
	{
		string result = TextFormatter.FormatWithRule(
			"abc123 ABC456",
			Rule(@"abc(\d+)", "X$1", MatchTypeEnum.RegEx));

		Assert.Equal("X123 X456", result);
	}

	[Fact]
	public void FormatWithRule_RegEx_CaseSensitive_RespectsCase()
	{
		string result = TextFormatter.FormatWithRule(
			"abc123 ABC456",
			Rule(@"abc(\d+)", "X$1", MatchTypeEnum.RegEx, caseSensitive: true));

		Assert.Equal("X123 ABC456", result);
	}

	// ----- FormatWithRule: Smart -----

	[Fact]
	public void FormatWithRule_Smart_ReplacesWordBoundaryMatch()
	{
		string result = TextFormatter.FormatWithRule(
			"Please ai is great.",
			Rule("ai", "AI", MatchTypeEnum.Smart));

		Assert.Contains("AI", result);
		Assert.DoesNotContain(" ai ", result);
	}

	[Fact]
	public void FormatWithRule_Smart_OnlyMatchesAtWordBoundary()
	{
		// Pins down current behavior: standalone "ai" is replaced; the "ai"
		// embedded inside "contain" is not. The Smart pattern consumes adjacent
		// whitespace as part of the match.
		string result = TextFormatter.FormatWithRule(
			"contain ai also paint",
			Rule("ai", "AI", MatchTypeEnum.Smart));

		Assert.Equal("containAIalso paint", result);
	}

	// ----- FormatWithRule: error paths -----

	[Fact]
	public void FormatWithRule_NullText_ReturnsNull()
	{
		Assert.Null(TextFormatter.FormatWithRule(null!, Rule("x", "y", MatchTypeEnum.Plain)));
	}

	[Fact]
	public void FormatWithRule_NullRule_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => TextFormatter.FormatWithRule("text", null!));
	}

	[Fact]
	public void FormatWithRule_UnknownMatchType_ThrowsNotImplemented()
	{
		var rule = new TranscriptFormatRule("a", "b", false, (MatchTypeEnum)999);
		Assert.Throws<NotImplementedException>(() => TextFormatter.FormatWithRule("text", rule));
	}

	// ----- CleanupPunctuation -----

	[Fact]
	public void CleanupPunctuation_NullInput_ReturnsNull()
	{
		string? input = null;
		Assert.Null(input!.CleanupPunctuation());
	}

	[Theory]
	// `.` and `,` are in the regex's "linker" character class `[ ,.]?`, so doubled
	// occurrences across a space collapse cleanly.
	[InlineData("Hello.. world", "Hello. world")]
	[InlineData("Hello,, world", "Hello, world")]
	// Other punctuation only collapses when the doubles are adjacent (no separator between them).
	[InlineData("Hello??world", "Hello? world")]
	[InlineData("Hello!!world", "Hello! world")]
	[InlineData("Hello;;world", "Hello; world")]
	[InlineData("Hello::world", "Hello: world")]
	public void CleanupPunctuation_DoubledPunctuationBetweenWords_Deduped(string input, string expected)
	{
		Assert.Equal(expected, input.CleanupPunctuation());
	}

	[Fact]
	public void CleanupPunctuation_TrailingPeriod_NormalizedWithSpace()
	{
		// Current behavior: a single trailing period is replaced by ". " (with trailing space).
		Assert.Equal("Hello, world. ", "Hello, world.".CleanupPunctuation());
	}

	[Fact]
	public void CleanupPunctuation_SinglePunctuationBetweenWords_Unchanged()
	{
		// "Hello, world" has no doubled punctuation — the regex normalizes the
		// existing comma+space to a comma+space (no visible change).
		Assert.Equal("Hello, world", "Hello, world".CleanupPunctuation());
	}

	[Fact]
	public void CleanupPunctuation_NoWordBoundary_LeavesUnchanged()
	{
		// pattern requires preceding word char, so leading "." is not deduped
		Assert.Equal("..", "..".CleanupPunctuation());
	}

	// ----- CleanLines (via FormatWithRules to exercise the pipeline) -----

	[Fact]
	public void FormatWithRules_StripsLeadingPunctuationFollowedByWhitespace()
	{
		// CleanLine's `^[,.;:]\s+` requires whitespace AFTER the leading punctuation.
		string input = ", hello" + Environment.NewLine + ". world";
		string result = input.FormatWithRules(new List<TranscriptFormatRule>());
		Assert.Equal("hello" + Environment.NewLine + "world", result);
	}

	[Fact]
	public void FormatWithRules_BulletWithLeadingPunctuationCollapsesToDashSpace()
	{
		string input = "- ,item one";
		string result = input.FormatWithRules(new List<TranscriptFormatRule>());
		Assert.Equal("- item one", result);
	}

	[Theory]
	[InlineData("foo, : ,bar", "foo:bar")]
	[InlineData("foo. : .bar", "foo:bar")]
	[InlineData("foo. : ,bar", "foo:bar")]
	[InlineData("foo, : .bar", "foo:bar")]
	public void FormatWithRules_CollapsesAlternatingColonPatterns(string input, string expected)
	{
		string result = input.FormatWithRules(new List<TranscriptFormatRule>());
		Assert.Equal(expected, result);
	}

	// ----- FormatWithRules -----

	[Fact]
	public void FormatWithRules_NullText_ReturnsNull()
	{
		string? input = null;
		string? result = input!.FormatWithRules(new List<TranscriptFormatRule>());
		Assert.Null(result);
	}

	[Fact]
	public void FormatWithRules_NullRules_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => "text".FormatWithRules(null!));
	}

	[Fact]
	public void FormatWithRules_AppliesRulesInOrder()
	{
		var rules = new List<TranscriptFormatRule>
		{
			Rule("a", "b", MatchTypeEnum.Plain),
			Rule("b", "c", MatchTypeEnum.Plain),
		};

		Assert.Equal("c", "a".FormatWithRules(rules));
	}

	[Fact]
	public void FormatWithRules_EmptyRules_StillRunsLineCleanup()
	{
		// Trim+leading punctuation strip should still run with no rules.
		// The leading punctuation strip requires whitespace after the punctuation.
		string input = "  , hello  ";
		string result = input.FormatWithRules(new List<TranscriptFormatRule>());
		Assert.Equal("hello", result);
	}

	// ----- RemoveSubstrings -----

	[Fact]
	public void RemoveSubstrings_NullText_ReturnsNull()
	{
		string? text = null;
		Assert.Null(text!.RemoveSubstrings("x"));
	}

	[Fact]
	public void RemoveSubstrings_NullArray_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => "text".RemoveSubstrings(null!));
	}

	[Fact]
	public void RemoveSubstrings_EmptyArray_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => "text".RemoveSubstrings(Array.Empty<string>()));
	}

	[Fact]
	public void RemoveSubstrings_RemovesAllOccurrencesOfSubstring()
	{
		Assert.Equal("Heo Word", "Hello World".RemoveSubstrings("l"));
	}

	[Fact]
	public void RemoveSubstrings_NoMatch_ReturnsInputUnchanged()
	{
		Assert.Equal("Hello World", "Hello World".RemoveSubstrings("xyz", "abc"));
	}

	[Fact]
	public void RemoveSubstrings_MultipleSubstrings_AllRemoved()
	{
		// "hello world" → remove "e" → "hllo world" → remove "wo" → "hllo rld"
		Assert.Equal("hllo rld", "hello world".RemoveSubstrings("e", "wo"));
	}
}
