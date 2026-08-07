using CognitiveSupport;
using static CognitiveSupport.TranscriptFormatRule;

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

		Assert.Equal("Please AI is great.", result);
	}

	[Fact]
	public void FormatWithRule_Smart_OnlyMatchesAtWordBoundary()
	{
		// Standalone "ai" is replaced; the "ai" embedded inside "contain" is not.
		// The spacing either side of the match survives — it used to be swallowed,
		// welding the neighbours into "containAIalso paint" (issue #222).
		string result = TextFormatter.FormatWithRule(
			"contain ai also paint",
			Rule("ai", "AI", MatchTypeEnum.Smart));

		Assert.Equal("contain AI also paint", result);
	}

	[Fact]
	public void FormatWithRule_Smart_KeepsPunctuationFollowingTheMatch()
	{
		string result = TextFormatter.FormatWithRule(
			"I like ai, mostly",
			Rule("ai", "AI", MatchTypeEnum.Smart));

		Assert.Equal("I like AI, mostly", result);
	}

	// The shipped default rules are dictated punctuation, and they rely on the replacement
	// supplying its own spacing: re-emitting the gap the match consumed would push the
	// period away from the word it closes.
	[Theory]
	[InlineData("Hello full stop next word", "full stop", ". ", "Hello. next word")]
	[InlineData("one comma two", "comma", ", ", "one, two")]
	[InlineData("done question mark really", "question mark", "? ", "done? really")]
	public void FormatWithRule_Smart_PunctuationReplacement_AttachesToThePrecedingWord(
		string input, string find, string replaceWith, string expected)
	{
		Assert.Equal(expected, TextFormatter.FormatWithRule(input, Rule(find, replaceWith, MatchTypeEnum.Smart)));
	}

	[Fact]
	public void FormatWithRule_Smart_NewLineReplacement_DoesNotStrandTheOriginalSpacing()
	{
		string result = TextFormatter.FormatWithRule(
			"first new line second",
			Rule("new line", Environment.NewLine, MatchTypeEnum.Smart));

		Assert.Equal("first" + Environment.NewLine + "second", result);
	}

	// A symbol replacement is spacing-neutral: it wants to weld its neighbours, which is the
	// one case where swallowing the gap was right all along.
	[Theory]
	[InlineData("state dash of dash the dash art", "dash", "-", "state-of-the-art")]
	[InlineData("and slash or", "slash", "/", "and/or")]
	[InlineData("jacques at sign example", "at sign", "@", "jacques@example")]
	[InlineData("my underscore var here", "underscore", "_", "my_var here")]
	public void FormatWithRule_Smart_SymbolReplacement_ClosesTheGap(
		string input, string find, string replaceWith, string expected)
	{
		Assert.Equal(expected, TextFormatter.FormatWithRule(input, Rule(find, replaceWith, MatchTypeEnum.Smart)));
	}

	// Closing punctuation finishes the word in front of it and lets the next word start
	// normally. Welding both sides — which is what every symbol used to get — ran the
	// following word straight into the mark (issue #293).
	[Theory]
	[InlineData("fifty percent last year", "percent", "%", "fifty% last year")]
	[InlineData("the total close bracket then", "close bracket", ")", "the total) then")]
	[InlineData("item close square then", "close square", "]", "item] then")]
	public void FormatWithRule_Smart_ClosingSymbol_HugsTheWordOnItsLeft(
		string input, string find, string replaceWith, string expected)
	{
		Assert.Equal(expected, TextFormatter.FormatWithRule(input, Rule(find, replaceWith, MatchTypeEnum.Smart)));
	}

	// Opening punctuation is the mirror image: it leans onto what follows and leaves the
	// gap in front of it alone.
	[Theory]
	[InlineData("issue hash 42 is open", "hash", "#", "issue #42 is open")]
	[InlineData("type open bracket here", "open bracket", "(", "type (here")]
	[InlineData("costs dollar 40 today", "dollar", "$", "costs $40 today")]
	public void FormatWithRule_Smart_OpeningSymbol_HugsTheWordOnItsRight(
		string input, string find, string replaceWith, string expected)
	{
		Assert.Equal(expected, TextFormatter.FormatWithRule(input, Rule(find, replaceWith, MatchTypeEnum.Smart)));
	}

	// A closing symbol at the end of a sentence still takes the punctuation that followed it,
	// so the mark it closes with is not stranded a space away.
	[Fact]
	public void FormatWithRule_Smart_ClosingSymbol_KeepsTrailingPunctuationAttached()
	{
		string result = TextFormatter.FormatWithRule(
			"we hit fifty percent. Then more",
			Rule("percent", "%", MatchTypeEnum.Smart));

		Assert.Equal("we hit fifty%. Then more", result);
	}

	// The shipped defaults are all left-attaching and all supply their own trailing space.
	// Handing them back the gap the match consumed as well would double it.
	[Theory]
	[InlineData("Hello full stop next word", "full stop", ". ", "Hello. next word")]
	[InlineData("one new colon two", "new colon", ": ", "one: two")]
	[InlineData("wait ellipsis then", "ellipsis", "... ", "wait... then")]
	public void FormatWithRule_Smart_ClosingSymbolSupplyingItsOwnSpace_DoesNotDoubleIt(
		string input, string find, string replaceWith, string expected)
	{
		Assert.Equal(expected, TextFormatter.FormatWithRule(input, Rule(find, replaceWith, MatchTypeEnum.Smart)));
	}

	// A possessive belongs to the word it possesses. It carries a letter, so it is a word
	// replacement rather than a symbol, but it still has to close the gap on its left.
	[Theory]
	[InlineData("that is Jacques apostrophe s laptop", "apostrophe s", "'s", "that is Jacques's laptop")]
	[InlineData("they are they apostrophe re here", "apostrophe re", "'re", "they are they're here")]
	[InlineData("we curly apostrophe s done", "curly apostrophe s", "’s", "we’s done")]
	public void FormatWithRule_Smart_ApostropheSuffix_AttachesToThePrecedingWord(
		string input, string find, string replaceWith, string expected)
	{
		Assert.Equal(expected, TextFormatter.FormatWithRule(input, Rule(find, replaceWith, MatchTypeEnum.Smart)));
	}

	// "Starts with an apostrophe" is not the same thing as "is a suffix". A decade and a
	// quotation both open with one and are both ordinary words that keep their gap.
	[Theory]
	[InlineData("back in the nineties we did", "nineties", "'90s", "back in the '90s we did")]
	[InlineData("he said quoted thing today", "quoted thing", "'quoted thing'", "he said 'quoted thing' today")]
	public void FormatWithRule_Smart_WordOpeningWithAnApostrophe_KeepsTheGap(
		string input, string find, string replaceWith, string expected)
	{
		Assert.Equal(expected, TextFormatter.FormatWithRule(input, Rule(find, replaceWith, MatchTypeEnum.Smart)));
	}

	// U+2019 is the right single quote and the typographic apostrophe at the same time.
	// Treating it as closing punctuation would break the far more common of the two uses,
	// so it keeps the both-sides behaviour it has always had.
	[Fact]
	public void FormatWithRule_Smart_CurlyApostropheSymbol_StillWeldsBothSides()
	{
		string result = TextFormatter.FormatWithRule(
			"that is John apostrophe s laptop",
			Rule("apostrophe", "’", MatchTypeEnum.Smart));

		Assert.Equal("that is John’s laptop", result);
	}

	// A replacement that opens with punctuation but carries text is a word, not punctuation,
	// and still needs the gap in front of it.
	[Fact]
	public void FormatWithRule_Smart_WordOpeningWithPunctuation_KeepsTheGap()
	{
		string result = TextFormatter.FormatWithRule(
			"built on dotnet today",
			Rule("dotnet", ".NET", MatchTypeEnum.Smart));

		Assert.Equal("built on .NET today", result);
	}

	// Deleting a word should close the gap to a single space, not to none — and not to one
	// space per copy when the word was said twice in a row.
	[Theory]
	[InlineData("so um yeah", "so yeah")]
	[InlineData("so um um yeah", "so yeah")]
	[InlineData("so um um um yeah", "so yeah")]
	[InlineData("so um  yeah", "so yeah")]
	[InlineData("um yeah", "yeah")]
	public void FormatWithRule_Smart_EmptyReplacement_ClosesToOneSpace(string input, string expected)
	{
		Assert.Equal(expected, TextFormatter.FormatWithRule(input, Rule("um", string.Empty, MatchTypeEnum.Smart)));
	}

	// The deleted word may be carrying the sentence's full stop. Dropping it as well loses the
	// sentence break; leaving it after the gap strands it before the next word.
	[Fact]
	public void FormatWithRule_Smart_EmptyReplacement_KeepsPunctuationTheDeletedWordCarried()
	{
		string result = TextFormatter.FormatWithRule(
			"I think um. Next one",
			Rule("um", string.Empty, MatchTypeEnum.Smart));

		Assert.Equal("I think. Next one", result);
	}

	// ...but with no word in front of it, that punctuation has nothing to attach to. A line
	// that opens with a run of punctuated fillers must not open with their leftover commas.
	[Theory]
	[InlineData("um, um, I think", "um", "I think")]
	[InlineData("um, um, um, I think", "um", "I think")]
	[InlineData("um. um. I think", "um", "I think")]
	[InlineData("you know, you know, I think", "you know", "I think")]
	[InlineData("so um, um, I think", "um", "so, I think")]
	public void FormatWithRule_Smart_EmptyReplacement_LeadingFillerRun_LeavesNoStrayPunctuation(
		string input, string find, string expected)
	{
		Assert.Equal(expected, TextFormatter.FormatWithRule(input, Rule(find, string.Empty, MatchTypeEnum.Smart)));
	}

	// The text after the deleted word may already start with a space of its own.
	[Fact]
	public void FormatWithRule_Smart_EmptyReplacement_DoesNotDoubleAnExistingSpace()
	{
		string result = TextFormatter.FormatWithRule(
			"I think um , next",
			Rule("um", string.Empty, MatchTypeEnum.Smart));

		Assert.Equal("I think , next", result);
	}

	// Smart is the literal match type. Regex metacharacters in Find used to reach the engine
	// raw: "C++" compiled to a nested quantifier and threw, aborting the whole formatting run
	// rather than just that rule (issue #222).
	[Theory]
	[InlineData("I know C++ well", "C++", "C plus plus", "I know C plus plus well")]
	[InlineData("type ( here", "(", "open bracket", "type open bracket here")]
	[InlineData("type [ here", "[", "left square", "type left square here")]
	[InlineData(@"a \ b", @"\", "backslash", "a backslash b")]
	public void FormatWithRule_Smart_RegexMetacharactersInFind_AreMatchedLiterally(
		string input, string find, string replaceWith, string expected)
	{
		Assert.Equal(expected, TextFormatter.FormatWithRule(input, Rule(find, replaceWith, MatchTypeEnum.Smart)));
	}

	[Fact]
	public void FormatWithRule_Smart_DollarInReplaceWith_IsInsertedLiterally()
	{
		string result = TextFormatter.FormatWithRule(
			"the cost is amount today",
			Rule("amount", "$1", MatchTypeEnum.Smart));

		Assert.Equal("the cost is $1 today", result);
	}

	// One bad rule used to take the whole run down with it, so every later rule was lost too.
	[Fact]
	public void FormatWithRules_SmartRuleWithMetacharacters_DoesNotAbortLaterRules()
	{
		var rules = new List<TranscriptFormatRule>
		{
			Rule("C++", "C plus plus", MatchTypeEnum.Smart),
			Rule("dotnet", ".NET", MatchTypeEnum.Smart),
		};

		Assert.Equal("C plus plus and .NET", "C++ and dotnet".FormatWithRules(rules));
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
	[InlineData("Hello.. world", "Hello. world")]
	[InlineData("Hello,, world", "Hello, world")]
	[InlineData("Hello?? world", "Hello? world")]
	[InlineData("Hello!! world", "Hello! world")]
	[InlineData("Hello;; world", "Hello; world")]
	[InlineData("Hello:: world", "Hello: world")]
	// Adjacent doubles (no whitespace between them) also collapse.
	[InlineData("Hello??world", "Hello? world")]
	[InlineData("Hello!!world", "Hello! world")]
	[InlineData("Hello;;world", "Hello; world")]
	[InlineData("Hello::world", "Hello: world")]
	public void CleanupPunctuation_DoubledPunctuationBetweenWords_Deduped(string input, string expected)
	{
		Assert.Equal(expected, input.CleanupPunctuation());
	}

	[Fact]
	public void CleanupPunctuation_TrailingPeriod_NoSpuriousTrailingSpace()
	{
		// Final punctuation at end-of-line should NOT have a trailing space appended.
		Assert.Equal("Hello, world.", "Hello, world.".CleanupPunctuation());
	}

	[Fact]
	public void CleanupPunctuation_SinglePunctuationBetweenWords_Unchanged()
	{
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
