using System;
using System.Collections.Generic;

namespace CognitiveSupport;

/// <summary>
/// Joins clips into one, with silence between them, so several sounds play one after the
/// other instead of on top of each other. The beep mixer plays every clip it is given at
/// once; two interval timers due at the same moment would otherwise blur into one sound and
/// the listener could not tell that two had played (issue #456).
/// </summary>
public static class BeepClipSequence
{
	public static readonly TimeSpan DefaultGap = TimeSpan.FromMilliseconds(250);

	public static BeepClip Join(IReadOnlyList<BeepClip> clips, TimeSpan gap)
	{
		ArgumentNullException.ThrowIfNull(clips);
		if (clips.Count == 0)
			throw new ArgumentException("At least one clip is needed.", nameof(clips));
		if (gap < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(gap));

		int sampleRate = clips[0].SampleRate;
		int channels = clips[0].Channels;
		if (clips.Count == 1)
			return clips[0];

		int gapSamples = (int)Math.Round(gap.TotalSeconds * sampleRate) * channels;
		long total = (long)gapSamples * (clips.Count - 1);
		foreach (var clip in clips)
		{
			if (clip.SampleRate != sampleRate || clip.Channels != channels)
				throw new ArgumentException("Every clip must have the same sample rate and channel count.", nameof(clips));
			total += clip.Samples.Length;
		}

		var samples = new float[total];
		int offset = 0;
		for (int i = 0; i < clips.Count; i++)
		{
			if (i > 0)
				offset += gapSamples; // Already zero: a new float array is silence.
			clips[i].Samples.Span.CopyTo(samples.AsSpan(offset));
			offset += clips[i].Samples.Length;
		}

		return new BeepClip(samples, sampleRate, channels);
	}
}
