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
}
