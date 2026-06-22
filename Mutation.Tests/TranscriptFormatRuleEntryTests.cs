using CognitiveSupport;
using Microsoft.UI.Xaml;
using Mutation.Ui;
using static CognitiveSupport.TranscriptFormatRule;

namespace Mutation.Tests;

public class TranscriptFormatRuleEntryTests
{
	[Fact]
	public void Ctor_NullRule_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new TranscriptFormatRuleEntry(null!));
	}

	[Fact]
	public void Properties_WriteThroughToBackingRule()
	{
		var rule = new TranscriptFormatRule();
		var entry = new TranscriptFormatRuleEntry(rule)
		{
			Find = "foo",
			ReplaceWith = "bar",
			CaseSensitive = true,
			MatchType = MatchTypeEnum.Smart,
		};

		Assert.Equal("foo", rule.Find);
		Assert.Equal("bar", rule.ReplaceWith);
		Assert.True(rule.CaseSensitive);
		Assert.Equal(MatchTypeEnum.Smart, rule.MatchType);
	}

	[Fact]
	public void NullBackingValues_SurfaceAsEmptyStrings()
	{
		var entry = new TranscriptFormatRuleEntry(new TranscriptFormatRule());

		Assert.Equal(string.Empty, entry.Find);
		Assert.Equal(string.Empty, entry.ReplaceWith);
	}

	[Fact]
	public void MatchTypeOptions_ContainsAllThree()
	{
		var entry = new TranscriptFormatRuleEntry(new TranscriptFormatRule());

		Assert.Equal(
			new[] { MatchTypeEnum.Plain, MatchTypeEnum.RegEx, MatchTypeEnum.Smart },
			entry.MatchTypeOptions);
	}

	[Fact]
	public void Regex_ValidPattern_HasNoError()
	{
		var rule = new TranscriptFormatRule("\\d+", "#", false, MatchTypeEnum.RegEx);
		var entry = new TranscriptFormatRuleEntry(rule);

		Assert.False(entry.HasRegexError);
		Assert.Null(entry.RegexError);
		Assert.Equal(Visibility.Collapsed, entry.RegexErrorVisibility);
	}

	[Fact]
	public void Regex_InvalidPattern_SurfacesError()
	{
		var rule = new TranscriptFormatRule("(unterminated", "x", false, MatchTypeEnum.RegEx);
		var entry = new TranscriptFormatRuleEntry(rule);

		Assert.True(entry.HasRegexError);
		Assert.False(string.IsNullOrEmpty(entry.RegexError));
		Assert.Equal(Visibility.Visible, entry.RegexErrorVisibility);
	}

	[Fact]
	public void InvalidPattern_OnlyValidatedWhenMatchTypeIsRegEx()
	{
		// A malformed regex is harmless for Plain matching, so no error should show.
		var entry = new TranscriptFormatRuleEntry(
			new TranscriptFormatRule("(unterminated", "x", false, MatchTypeEnum.Plain));
		Assert.False(entry.HasRegexError);

		// Switching the same bad pattern to RegEx must surface the error.
		entry.MatchType = MatchTypeEnum.RegEx;
		Assert.True(entry.HasRegexError);

		// Switching back clears it.
		entry.MatchType = MatchTypeEnum.Plain;
		Assert.False(entry.HasRegexError);
	}

	[Fact]
	public void EditingFind_RevalidatesRegex()
	{
		var entry = new TranscriptFormatRuleEntry(
			new TranscriptFormatRule("(unterminated", "x", false, MatchTypeEnum.RegEx));
		Assert.True(entry.HasRegexError);

		entry.Find = "valid";
		Assert.False(entry.HasRegexError);
	}
}
