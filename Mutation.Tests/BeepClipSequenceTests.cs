using System;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

public class BeepClipSequenceTests
{
	private static BeepClip Clip(params float[] samples) => new(samples, sampleRate: 4, channels: 2);

	[Fact]
	public void OneClip_IsReturnedAsIs()
	{
		var clip = Clip(1, 1);

		Assert.Same(clip, BeepClipSequence.Join(new[] { clip }, TimeSpan.FromSeconds(1)));
	}

	[Fact]
	public void TwoClips_PlayOneAfterTheOther_WithSilenceBetween()
	{
		// 4 frames per second, 2 channels: a half-second gap is 2 frames, 4 samples.
		var joined = BeepClipSequence.Join(new[] { Clip(1, 1), Clip(2, 2, 3, 3) }, TimeSpan.FromSeconds(0.5));

		Assert.Equal(new float[] { 1, 1, 0, 0, 0, 0, 2, 2, 3, 3 }, joined.Samples.ToArray());
		Assert.Equal(4, joined.SampleRate);
		Assert.Equal(2, joined.Channels);
	}

	[Fact]
	public void ClipsInDifferentFormats_AreRefused()
	{
		var other = new BeepClip(new float[] { 1 }, sampleRate: 8, channels: 1);

		Assert.Throws<ArgumentException>(() => BeepClipSequence.Join(new[] { Clip(1, 1), other }, TimeSpan.Zero));
	}

	[Fact]
	public void NoClips_AreRefused()
	{
		Assert.Throws<ArgumentException>(() => BeepClipSequence.Join(Array.Empty<BeepClip>(), TimeSpan.Zero));
	}
}
