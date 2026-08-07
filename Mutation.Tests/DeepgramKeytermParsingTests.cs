using CognitiveSupport;

namespace Mutation.Tests;

// Keyterms are how the user teaches Deepgram the names and jargon it would otherwise get
// wrong — which is exactly the text most likely to contain an abbreviation period. The
// parser used to stop at the first period anywhere in the prompt, so "Dr. Bosch" reduced
// the whole list to "Dr", and a prompt with no closing period lost every term (issue #245).
public class DeepgramKeytermParsingTests
{
	[Fact]
	public void AbbreviationPeriod_DoesNotTruncateTheList()
	{
		var keyterms = DeepgramSpeechToTextService.ParseKeyterms("keyterms: Dr. Bosch, Mutation, WinUI.");

		Assert.Equal(new[] { "Dr. Bosch", "Mutation", "WinUI" }, keyterms);
	}

	[Fact]
	public void NoClosingPeriod_StillReadsTheList()
	{
		var keyterms = DeepgramSpeechToTextService.ParseKeyterms("keyterms: Mutation, WinUI");

		Assert.Equal(new[] { "Mutation", "WinUI" }, keyterms);
	}

	[Fact]
	public void ClosingPeriod_IsNotPartOfTheLastTerm()
	{
		var keyterms = DeepgramSpeechToTextService.ParseKeyterms("keyterms: Mutation, WinUI.");

		Assert.Equal(new[] { "Mutation", "WinUI" }, keyterms);
	}

	[Fact]
	public void TrailingSpacesAfterTheClosingPeriod_AreIgnored()
	{
		var keyterms = DeepgramSpeechToTextService.ParseKeyterms("keyterms: Mutation, WinUI.   ");

		Assert.Equal(new[] { "Mutation", "WinUI" }, keyterms);
	}

	// The list belongs to its own line, so prose on the lines around it is not swept in.
	[Fact]
	public void ListEndsAtTheEndOfItsLine()
	{
		string prompt = string.Join(Environment.NewLine, new[]
		{
			"Transcribe carefully.",
			"keyterms: Dr. Bosch, Deepgram.",
			"Ignore background noise.",
		});

		var keyterms = DeepgramSpeechToTextService.ParseKeyterms(prompt);

		Assert.Equal(new[] { "Dr. Bosch", "Deepgram" }, keyterms);
	}

	[Fact]
	public void LabelIsMatchedRegardlessOfCase()
	{
		var keyterms = DeepgramSpeechToTextService.ParseKeyterms("KEYTERMS: Mutation");

		Assert.Equal(new[] { "Mutation" }, keyterms);
	}

	[Theory]
	[InlineData("")]
	[InlineData("Transcribe this recording accurately.")]
	[InlineData(null)]
	public void NoKeytermsLabel_YieldsNoTerms(string? prompt)
	{
		Assert.Empty(DeepgramSpeechToTextService.ParseKeyterms(prompt!));
	}

	[Fact]
	public void EmptyEntriesAndSurroundingSpace_AreDropped()
	{
		var keyterms = DeepgramSpeechToTextService.ParseKeyterms("keyterms:  Mutation ,, WinUI ,");

		Assert.Equal(new[] { "Mutation", "WinUI" }, keyterms);
	}
}
