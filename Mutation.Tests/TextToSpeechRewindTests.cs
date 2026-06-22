using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// Verifies the pure word-rewind used by TextToSpeechService when resuming playback.
// Resuming rewinds a configurable number of words before the stop point so the
// listener regains context of where they are.
public class TextToSpeechRewindTests
{
	private const string Text = "The quick brown fox jumps over the lazy dog";
	//                           0123456789...
	// Word starts: The=0 quick=4 brown=10 fox=16 jumps=20 over=26 the=31 lazy=35 dog=40

	[Fact]
	public void ZeroWords_ReturnsSamePosition()
	{
		Assert.Equal(20, TextToSpeechService.RewindByWords(Text, 20, 0));
	}

	[Fact]
	public void NegativeWords_ReturnsSamePosition()
	{
		Assert.Equal(20, TextToSpeechService.RewindByWords(Text, 20, -3));
	}

	[Fact]
	public void RewindsThreeWordsFromWordBoundary()
	{
		// Stopped at the start of "over" (26). Back 3 words (jumps, fox, brown) ->
		// start of "brown" (10).
		Assert.Equal(10, TextToSpeechService.RewindByWords(Text, 26, 3));
	}

	[Fact]
	public void RewindOneWord_LandsOnPreviousWordStart()
	{
		// Start of "over" (26) back 1 word -> start of "jumps" (20).
		Assert.Equal(20, TextToSpeechService.RewindByWords(Text, 26, 1));
	}

	[Fact]
	public void MidWord_CountsPartialWordAsFirstStep()
	{
		// Position 23 is inside "jumps" (20-24). Back 1 word steps over the partial
		// word to its start (20).
		Assert.Equal(20, TextToSpeechService.RewindByWords(Text, 23, 1));
	}

	[Fact]
	public void MoreWordsThanAvailable_ClampsToStart()
	{
		Assert.Equal(0, TextToSpeechService.RewindByWords(Text, 26, 99));
	}

	[Fact]
	public void PositionAtStart_StaysAtStart()
	{
		Assert.Equal(0, TextToSpeechService.RewindByWords(Text, 0, 3));
	}

	[Fact]
	public void PositionPastEnd_IsClampedThenRewinds()
	{
		// Position beyond the text clamps to length, then steps back 1 word -> "dog" (40).
		Assert.Equal(40, TextToSpeechService.RewindByWords(Text, Text.Length + 10, 1));
	}

	[Fact]
	public void HandlesMultipleSpacesBetweenWords()
	{
		const string spaced = "alpha   beta   gamma";
		// alpha=0  beta=8  gamma=15
		// Back 2 words from end (20) -> start of "beta" (8).
		Assert.Equal(8, TextToSpeechService.RewindByWords(spaced, spaced.Length, 2));
	}
}
