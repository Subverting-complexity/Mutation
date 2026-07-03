namespace CognitiveSupport;

// Synthesizes Console.Beep-like square-wave tone sequences into in-memory WAV
// data so default beeps can be played through the non-blocking SoundPlayer
// path instead of the thread-blocking Console.Beep API.
public static class BeepToneSynthesizer
{
	public const int SampleRate = 22050;
	public const int InterSegmentGapMs = 50;
	private const short Amplitude = 8000;
	private const int FadeMs = 5;

	// Builds a complete PCM 16-bit mono WAV containing the given tone segments,
	// separated by short silences so consecutive beeps stay audibly distinct.
	public static byte[] SynthesizeWav(IReadOnlyList<(int Frequency, int DurationMs)> segments)
	{
		ArgumentNullException.ThrowIfNull(segments);
		if (segments.Count == 0)
			throw new ArgumentException("At least one tone segment is required.", nameof(segments));

		var samples = new List<short>();
		for (var i = 0; i < segments.Count; i++)
		{
			if (i > 0)
				samples.AddRange(new short[MsToSamples(InterSegmentGapMs)]);
			AppendTone(samples, segments[i].Frequency, segments[i].DurationMs);
		}

		return WrapInWavContainer(samples);
	}

	private static int MsToSamples(int milliseconds) => SampleRate * milliseconds / 1000;

	private static void AppendTone(List<short> samples, int frequency, int durationMs)
	{
		if (frequency <= 0)
			throw new ArgumentOutOfRangeException(nameof(frequency));
		if (durationMs <= 0)
			throw new ArgumentOutOfRangeException(nameof(durationMs));

		var count = MsToSamples(durationMs);
		var fadeSamples = Math.Min(MsToSamples(FadeMs), count / 2);
		var samplesPerHalfPeriod = Math.Max(1, SampleRate / (2 * frequency));

		for (var i = 0; i < count; i++)
		{
			// Square wave, matching the timbre of Console.Beep.
			var value = ((i / samplesPerHalfPeriod) % 2 == 0) ? Amplitude : (short)-Amplitude;

			// Linear fade at both ends to avoid audible clicks.
			double gain = 1.0;
			if (fadeSamples > 0)
			{
				if (i < fadeSamples)
					gain = (double)i / fadeSamples;
				else if (i >= count - fadeSamples)
					gain = (double)(count - 1 - i) / fadeSamples;
			}
			samples.Add((short)(value * gain));
		}
	}

	private static byte[] WrapInWavContainer(List<short> samples)
	{
		const short bitsPerSample = 16;
		const short channels = 1;
		var dataByteCount = samples.Count * 2;

		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		writer.Write("RIFF"u8);
		writer.Write(36 + dataByteCount);
		writer.Write("WAVE"u8);
		writer.Write("fmt "u8);
		writer.Write(16); // fmt chunk size
		writer.Write((short)1); // PCM
		writer.Write(channels);
		writer.Write(SampleRate);
		writer.Write(SampleRate * channels * bitsPerSample / 8); // byte rate
		writer.Write((short)(channels * bitsPerSample / 8)); // block align
		writer.Write(bitsPerSample);
		writer.Write("data"u8);
		writer.Write(dataByteCount);
		foreach (var sample in samples)
			writer.Write(sample);
		writer.Flush();
		return stream.ToArray();
	}
}
