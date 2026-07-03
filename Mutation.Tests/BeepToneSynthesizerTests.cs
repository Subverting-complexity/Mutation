using CognitiveSupport;

namespace Mutation.Tests;

public class BeepToneSynthesizerTests
{
	private static byte[] Wav(params (int Frequency, int DurationMs)[] segments)
		=> BeepToneSynthesizer.SynthesizeWav(segments);

	private static short[] Samples(byte[] wav)
	{
		const int headerBytes = 44;
		var samples = new short[(wav.Length - headerBytes) / 2];
		Buffer.BlockCopy(wav, headerBytes, samples, 0, wav.Length - headerBytes);
		return samples;
	}

	[Fact]
	public void SynthesizeWav_ProducesValidRiffWaveHeader()
	{
		var wav = Wav((970, 80));

		Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
		Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
		Assert.Equal("fmt ", System.Text.Encoding.ASCII.GetString(wav, 12, 4));
		Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));
		Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
		Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));
		Assert.Equal(1, BitConverter.ToInt16(wav, 20)); // PCM
		Assert.Equal(1, BitConverter.ToInt16(wav, 22)); // mono
		Assert.Equal(BeepToneSynthesizer.SampleRate, BitConverter.ToInt32(wav, 24));
		Assert.Equal(16, BitConverter.ToInt16(wav, 34)); // bits per sample
	}

	[Fact]
	public void SynthesizeWav_SingleSegment_HasExpectedSampleCount()
	{
		var wav = Wav((500, 200));

		var expected = BeepToneSynthesizer.SampleRate * 200 / 1000;
		Assert.Equal(expected, Samples(wav).Length);
	}

	[Fact]
	public void SynthesizeWav_MultiSegment_IncludesGapsBetweenSegments()
	{
		var wav = Wav((300, 100), (300, 100), (300, 100));

		var toneSamples = BeepToneSynthesizer.SampleRate * 100 / 1000;
		var gapSamples = BeepToneSynthesizer.SampleRate * BeepToneSynthesizer.InterSegmentGapMs / 1000;
		Assert.Equal(3 * toneSamples + 2 * gapSamples, Samples(wav).Length);
	}

	[Fact]
	public void SynthesizeWav_ToneIsNotSilent()
	{
		var samples = Samples(Wav((970, 80)));

		Assert.Contains(samples, s => Math.Abs((int)s) > 1000);
	}

	[Fact]
	public void SynthesizeWav_GapBetweenSegmentsIsSilent()
	{
		var samples = Samples(Wav((300, 100), (300, 100)));

		var toneSamples = BeepToneSynthesizer.SampleRate * 100 / 1000;
		var gapSamples = BeepToneSynthesizer.SampleRate * BeepToneSynthesizer.InterSegmentGapMs / 1000;
		var gap = samples.Skip(toneSamples).Take(gapSamples);
		Assert.All(gap, s => Assert.Equal(0, s));
	}

	[Fact]
	public void SynthesizeWav_EmptySequence_Throws()
	{
		Assert.Throws<ArgumentException>(() => Wav());
	}

	[Theory]
	[InlineData(0, 100)]
	[InlineData(440, 0)]
	public void SynthesizeWav_InvalidSegment_Throws(int frequency, int durationMs)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Wav((frequency, durationMs)));
	}

	[Theory]
	[InlineData(BeepType.Start)]
	[InlineData(BeepType.Success)]
	[InlineData(BeepType.Failure)]
	[InlineData(BeepType.End)]
	[InlineData(BeepType.Mute)]
	[InlineData(BeepType.Unmute)]
	public void GetDefaultSequence_EveryBeepType_SynthesizesValidWav(BeepType type)
	{
		var sequence = BeepPlayer.GetDefaultSequence(type);

		Assert.NotEmpty(sequence);
		var wav = BeepToneSynthesizer.SynthesizeWav(sequence);
		Assert.True(wav.Length > 44);
	}

	[Fact]
	public void GetDefaultSequence_Failure_RepeatsThreeTimes()
	{
		var sequence = BeepPlayer.GetDefaultSequence(BeepType.Failure);

		Assert.Equal(BeepPlayer.DefaultFailureRepeats, sequence.Count);
		Assert.All(sequence, s => Assert.Equal((BeepPlayer.DefaultFailureFrequency, BeepPlayer.DefaultFailureDuration), s));
	}
}
