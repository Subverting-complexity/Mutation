using System.Linq;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// The sound a dictation press makes when the microphone is not open yet (issue #312).
///
/// <para>
/// It exists because the alternatives were both wrong for someone driving Mutation by
/// ear from another window. Silence reads as a shortcut that did not register. The Start
/// beep says the microphone is live, and a user who believes that talks into nothing for
/// as long as the device takes.
/// </para>
/// </summary>
public class WaitingBeepTests
{
	[Fact]
	public void The_waiting_beep_has_a_default_sequence()
	{
		// Every beep the app can play must synthesize; GetDefaultSequence throws on a type
		// that was added to the enum and nowhere else.
		Assert.NotEmpty(BeepPlayer.GetDefaultSequence(BeepType.Waiting));
	}

	[Fact]
	public void The_waiting_beep_is_not_the_start_beep()
	{
		Assert.NotEqual(
			BeepPlayer.GetDefaultSequence(BeepType.Start),
			BeepPlayer.GetDefaultSequence(BeepType.Waiting));
	}

	// Success rises and Waiting falls. Both are two tones, so the direction is the only
	// thing telling them apart, and "ready" and "not ready" are the two answers the user
	// most needs to keep separate.
	[Fact]
	public void The_waiting_beep_falls_where_the_success_beep_rises()
	{
		var waiting = BeepPlayer.GetDefaultSequence(BeepType.Waiting);
		var success = BeepPlayer.GetDefaultSequence(BeepType.Success);

		Assert.True(waiting.Count > 1, "Waiting is a two-tone beep; the fixture assumes so.");
		Assert.True(waiting.Last().Frequency < waiting.First().Frequency, "Waiting must fall.");
		Assert.True(success.Last().Frequency > success.First().Frequency, "Success must rise.");
	}

	// It is lower than every beep that reports a finished action, so it does not read as
	// one of them.
	[Fact]
	public void The_waiting_beep_sits_below_the_beeps_that_report_completion()
	{
		int highestWaitingTone = BeepPlayer.GetDefaultSequence(BeepType.Waiting).Max(tone => tone.Frequency);

		Assert.True(highestWaitingTone < BeepPlayer.DefaultStartFrequency);
		Assert.True(highestWaitingTone < BeepPlayer.GetDefaultSequence(BeepType.Success).Min(tone => tone.Frequency));
	}

	[Fact]
	public void The_waiting_beep_synthesizes()
	{
		Assert.NotEmpty(BeepToneSynthesizer.SynthesizeWav(BeepPlayer.GetDefaultSequence(BeepType.Waiting)));
	}
}
