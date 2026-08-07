using System;
using CognitiveSupport;
using static CognitiveSupport.TranscriptFormatRule;

namespace Mutation.Tests;

/// <summary>
/// Pins the output of every Smart rule Mutation seeds into a new settings file. Issue #293
/// required that teaching the formatter which side a symbol attaches to leave these
/// byte-identical, and they are the rules almost every user is actually running.
/// <para>
/// The expectations here were taken from the behaviour before the change, so a future edit to
/// the classification that shifts one of them fails here rather than in someone's transcript.
/// Keep this list in step with the seeding in <c>SettingsManager</c>.
/// </para>
/// </summary>
public class SmartRuleShippedDefaultsTests
{
	private static string Apply(string input, string find, string replaceWith)
		=> TextFormatter.FormatWithRule(input, new TranscriptFormatRule(find, replaceWith, false, MatchTypeEnum.Smart));

	private static readonly string NewLine = Environment.NewLine;

	[Theory]
	[InlineData("new colon", ": ", "one new colon two", "one: two")]
	[InlineData("semicolon", "; ", "one semicolon two", "one; two")]
	[InlineData("full stop", ". ", "one full stop two", "one. two")]
	[InlineData("comma", ", ", "one comma two", "one, two")]
	[InlineData("exclamation mark", "! ", "one exclamation mark two", "one! two")]
	[InlineData("question mark", "? ", "one question mark two", "one? two")]
	[InlineData("ellipsis", "... ", "one ellipsis two", "one... two")]
	[InlineData("dot dot dot", "... ", "one dot dot dot two", "one... two")]
	public void Punctuation_defaults_attach_to_the_word_before_and_supply_their_own_space(
		string find, string replaceWith, string input, string expected)
	{
		Assert.Equal(expected, Apply(input, find, replaceWith));
	}

	[Theory]
	[InlineData("new line", "one new line two")]
	[InlineData("newline", "one newline two")]
	[InlineData("next line", "one next line two")]
	public void Line_break_defaults_swallow_the_spacing_either_side(string find, string input)
	{
		Assert.Equal($"one{NewLine}two", Apply(input, find, NewLine));
	}

	[Theory]
	[InlineData("new paragraph", "one new paragraph two")]
	[InlineData("new paragraphs", "one new paragraphs two")]
	[InlineData("next paragraph", "one next paragraph two")]
	public void Paragraph_defaults_swallow_the_spacing_either_side(string find, string input)
	{
		Assert.Equal($"one{NewLine}{NewLine}two", Apply(input, find, NewLine + NewLine));
	}

	[Theory]
	[InlineData("new bullet", "one new bullet two")]
	[InlineData("next bullet", "one next bullet two")]
	public void Bullet_defaults_swallow_the_spacing_either_side(string find, string input)
	{
		Assert.Equal($"one{NewLine}- two", Apply(input, find, NewLine + "- "));
	}
}
