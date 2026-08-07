using System.Collections.Generic;
using CognitiveSupport;

namespace Mutation.Tests;

public class SilenceTrimmerTests
{
	private const int SampleRate = 48000;
	private const int FrameSize = 960; // 20ms

	// threshold 0.2s = 10 frames; guard 40ms = 2 frames.
	private static SilenceTrimmer NewTrimmer() =>
		new(SampleRate, FrameSize, new SilenceTrimmerOptions(
			SilenceThresholdDbFs: -40.0,
			MinSilenceSeconds: 0.2,
			GuardMilliseconds: 40.0));

	private static short[] Silent() => new short[FrameSize];

	private static short[] Loud()
	{
		var f = new short[FrameSize];
		for (int i = 0; i < f.Length; i++) f[i] = 8000;
		return f;
	}

	private static List<short[]> Run(SilenceTrimmer trimmer, IEnumerable<short[]> frames)
	{
		var emitted = new List<short[]>();
		foreach (var f in frames)
			trimmer.ProcessFrame(f, emitted.Add);
		trimmer.Flush(emitted.Add);
		return emitted;
	}

	private static IEnumerable<short[]> Repeat(short[] frame, int count)
	{
		for (int i = 0; i < count; i++) yield return frame;
	}

	[Fact]
	public void AllSilence_EmitsNothing()
	{
		var trimmer = NewTrimmer();
		var emitted = Run(trimmer, Repeat(Silent(), 15));

		Assert.Empty(emitted);
		Assert.Equal(0, trimmer.SpeechFrameCount);
	}

	[Fact]
	public void InteriorGap_TrimmedToThreshold()
	{
		var trimmer = NewTrimmer();
		var frames = new List<short[]>();
		frames.AddRange(Repeat(Loud(), 5));
		frames.AddRange(Repeat(Silent(), 30)); // > threshold (10)
		frames.AddRange(Repeat(Loud(), 5));

		var emitted = Run(trimmer, frames);

		// 5 speech + 10 kept silence + 5 speech
		Assert.Equal(20, emitted.Count);
		Assert.Equal(10, trimmer.SpeechFrameCount);
	}

	[Fact]
	public void ShortGap_LeftUntouched()
	{
		var trimmer = NewTrimmer();
		var frames = new List<short[]>();
		frames.AddRange(Repeat(Loud(), 5));
		frames.AddRange(Repeat(Silent(), 6)); // <= threshold (10)
		frames.AddRange(Repeat(Loud(), 5));

		var emitted = Run(trimmer, frames);

		Assert.Equal(16, emitted.Count); // nothing stripped
	}

	[Fact]
	public void LeadingAndTrailingSilence_TrimmedToGuard()
	{
		var trimmer = NewTrimmer();
		var frames = new List<short[]>();
		frames.AddRange(Repeat(Silent(), 20));
		frames.AddRange(Repeat(Loud(), 5));
		frames.AddRange(Repeat(Silent(), 20));

		var emitted = Run(trimmer, frames);

		// 2 guard (pre-roll) + 5 speech + 2 guard (hang-time)
		Assert.Equal(9, emitted.Count);
		Assert.Equal(5, trimmer.SpeechFrameCount);
	}

	[Fact]
	public void ContinuousSpeech_PassesThrough()
	{
		var trimmer = NewTrimmer();
		var emitted = Run(trimmer, Repeat(Loud(), 12));

		Assert.Equal(12, emitted.Count);
		Assert.Equal(12, trimmer.SpeechFrameCount);
	}

	// One frame is 20ms, so a frame count reads straight off as milliseconds.
	private static TimeSpan Frames(int count) => TimeSpan.FromMilliseconds(count * 20);

	[Fact]
	public void ShortGap_RecordsNoRemoval()
	{
		var trimmer = NewTrimmer();
		var frames = new List<short[]>();
		frames.AddRange(Repeat(Loud(), 5));
		frames.AddRange(Repeat(Silent(), 6)); // <= threshold, nothing is dropped
		frames.AddRange(Repeat(Loud(), 5));

		Run(trimmer, frames);

		Assert.Empty(trimmer.RemovedSilences);
	}

	[Fact]
	public void InteriorGap_RecordsWhereItWasCutAndHowMuchWentMissing()
	{
		var trimmer = NewTrimmer();
		var frames = new List<short[]>();
		frames.AddRange(Repeat(Loud(), 5));
		frames.AddRange(Repeat(Silent(), 30)); // 10 frames survive, 20 are dropped
		frames.AddRange(Repeat(Loud(), 5));

		Run(trimmer, frames);

		var removal = Assert.Single(trimmer.RemovedSilences);
		// 5 speech frames, then the 8 frames of hang-time kept before the cut.
		Assert.Equal(Frames(13), removal.Position);
		Assert.Equal(Frames(20), removal.RemovedDuration);
	}

	[Fact]
	public void LaterRemovals_ArePositionedOnTheShortenedOutput_NotTheOriginal()
	{
		var trimmer = NewTrimmer();
		var frames = new List<short[]>();
		frames.AddRange(Repeat(Loud(), 5));
		frames.AddRange(Repeat(Silent(), 30)); // 20 dropped
		frames.AddRange(Repeat(Loud(), 5));
		frames.AddRange(Repeat(Silent(), 40)); // 30 dropped
		frames.AddRange(Repeat(Loud(), 5));

		var emitted = Run(trimmer, frames);

		Assert.Equal(2, trimmer.RemovedSilences.Count);

		// In the source the second gap begins at frame 40. Twenty frames were already
		// removed ahead of it, so on the trimmed timeline the cut sits at frame 28.
		Assert.Equal(Frames(13), trimmer.RemovedSilences[0].Position);
		Assert.Equal(Frames(28), trimmer.RemovedSilences[1].Position);
		Assert.Equal(Frames(30), trimmer.RemovedSilences[1].RemovedDuration);

		// Every recorded position has to be somewhere inside the audio actually written.
		foreach (var removal in trimmer.RemovedSilences)
			Assert.InRange(removal.Position, TimeSpan.Zero, Frames(emitted.Count));
	}

	[Fact]
	public void LeadingAndTrailingSilence_AreRecordedToo()
	{
		var trimmer = NewTrimmer();
		var frames = new List<short[]>();
		frames.AddRange(Repeat(Silent(), 20));
		frames.AddRange(Repeat(Loud(), 5));
		frames.AddRange(Repeat(Silent(), 20));

		Run(trimmer, frames);

		Assert.Equal(2, trimmer.RemovedSilences.Count);

		// Leading silence is cut before anything has been written.
		Assert.Equal(TimeSpan.Zero, trimmer.RemovedSilences[0].Position);
		Assert.Equal(Frames(18), trimmer.RemovedSilences[0].RemovedDuration);

		// Trailing: 2 pre-roll + 5 speech + 2 hang-time have been written by then.
		Assert.Equal(Frames(9), trimmer.RemovedSilences[1].Position);
		Assert.Equal(Frames(18), trimmer.RemovedSilences[1].RemovedDuration);
	}

	[Fact]
	public void AllSilence_IsRecordedAsOneRemovalAtTheStart()
	{
		var trimmer = NewTrimmer();
		Run(trimmer, Repeat(Silent(), 15));

		// Nothing was written, so the only position the cut can sit at is the start.
		var removal = Assert.Single(trimmer.RemovedSilences);
		Assert.Equal(TimeSpan.Zero, removal.Position);
		Assert.Equal(Frames(15), removal.RemovedDuration);
	}
}
