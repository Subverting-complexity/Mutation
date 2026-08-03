using CognitiveSupport;

namespace Mutation.Tests;

// Issue #216: retry progress used to be signalled by calling BeepPlayer.Play in a loop,
// which the 500 ms duplicate-suppression window collapsed down to one audible beep. The
// repeat now has to reach the synthesizer as a single multi-tone sequence.
public class RetryBeepTests
{
	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(5)]
	public void GetRepeatedSequence_ScalesWithRepeatCount(int repeatCount)
	{
		var single = BeepPlayer.GetDefaultSequence(BeepType.End);

		var repeated = BeepPlayer.GetRepeatedSequence(BeepType.End, repeatCount);

		Assert.Equal(single.Count * repeatCount, repeated.Count);
	}

	[Fact]
	public void GetRepeatedSequence_RepeatsTheSameTones()
	{
		var single = BeepPlayer.GetDefaultSequence(BeepType.End);

		var repeated = BeepPlayer.GetRepeatedSequence(BeepType.End, 3);

		for (int i = 0; i < repeated.Count; i++)
			Assert.Equal(single[i % single.Count], repeated[i]);
	}

	[Fact]
	public void GetRepeatedSequence_ThreeRetriesAreLongerThanOne()
	{
		var once = BeepToneSynthesizer.SynthesizeWav(BeepPlayer.GetRepeatedSequence(BeepType.End, 1));
		var thrice = BeepToneSynthesizer.SynthesizeWav(BeepPlayer.GetRepeatedSequence(BeepType.End, 3));

		Assert.True(thrice.Length > once.Length,
			"Three retries must be audibly distinguishable from one.");
	}

	[Fact]
	public void GetRepeatedSequence_MultiToneBeepRepeatsWholeSequence()
	{
		var single = BeepPlayer.GetDefaultSequence(BeepType.Success);
		Assert.True(single.Count > 1, "Success is a multi-tone beep; the fixture assumes so.");

		var repeated = BeepPlayer.GetRepeatedSequence(BeepType.Success, 2);

		Assert.Equal(single.Count * 2, repeated.Count);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-4)]
	public void GetRepeatedSequence_NonPositiveCountFallsBackToASingleBeep(int repeatCount)
	{
		var repeated = BeepPlayer.GetRepeatedSequence(BeepType.End, repeatCount);

		Assert.Equal(BeepPlayer.GetDefaultSequence(BeepType.End).Count, repeated.Count);
	}

	[Fact]
	public void ShouldBeep_IsSilentOnTheFirstNonRetryAttempt()
	{
		Assert.False(RetryBeepPolicy.ShouldBeep(1));
		Assert.Equal(0, RetryBeepPolicy.RepeatCountFor(1));
	}

	[Theory]
	[InlineData(2, 2)]
	[InlineData(3, 3)]
	[InlineData(4, 4)]
	public void RepeatCountFor_GivesOneBeepPerRetryAttempt(int attempt, int expectedBeeps)
	{
		Assert.True(RetryBeepPolicy.ShouldBeep(attempt));
		Assert.Equal(expectedBeeps, RetryBeepPolicy.RepeatCountFor(attempt));
	}

	[Fact]
	public void GetRepeatedSequence_ClampsRunawayRetryCounts()
	{
		var single = BeepPlayer.GetDefaultSequence(BeepType.End);

		var repeated = BeepPlayer.GetRepeatedSequence(BeepType.End, BeepPlayer.MaxRepeatCount + 50);

		Assert.Equal(single.Count * BeepPlayer.MaxRepeatCount, repeated.Count);
	}
}
