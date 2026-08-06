using System;
using System.Collections.Generic;
using CognitiveSupport;
using NAudio.Wave;

namespace Mutation.Tests;

public class SoundTouchSampleProviderTests
{
	private const int SampleRate = 48000;

	// A one-second 440 Hz mono tone as float samples, used as a deterministic source.
	private static float[] BuildTone(int frames)
	{
		var data = new float[frames];
		for (int i = 0; i < frames; i++)
			data[i] = 0.5f * (float)Math.Sin(2.0 * Math.PI * 440.0 * i / SampleRate);
		return data;
	}

	private static int DrainFrameCount(ISampleProvider provider, out float peak)
	{
		peak = 0f;
		var buffer = new float[SampleRate]; // 1s chunks
		int totalFrames = 0;
		int read;
		while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
		{
			totalFrames += read / provider.WaveFormat.Channels;
			for (int i = 0; i < read; i++)
				peak = Math.Max(peak, Math.Abs(buffer[i]));
		}

		return totalFrames;
	}

	// Everything the provider produced, kept so the output's pitch can be measured.
	private static float[] Drain(ISampleProvider provider)
	{
		var output = new List<float>();
		var buffer = new float[SampleRate];
		int read;
		while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
			output.AddRange(buffer.AsSpan(0, read).ToArray());

		return output.ToArray();
	}

	// Goertzel: the energy at one frequency, without pulling in an FFT. Comparing the
	// energy at 440 Hz against its octaves is enough to say the pitch did not move.
	private static double EnergyAt(ReadOnlySpan<float> samples, double frequency)
	{
		double coefficient = 2.0 * Math.Cos(2.0 * Math.PI * frequency / SampleRate);
		double previous = 0.0;
		double beforeThat = 0.0;

		foreach (float sample in samples)
		{
			double current = sample + (coefficient * previous) - beforeThat;
			beforeThat = previous;
			previous = current;
		}

		return Math.Sqrt(Math.Abs((previous * previous) + (beforeThat * beforeThat) - (coefficient * previous * beforeThat)));
	}

	[Fact]
	public void WaveFormat_IsPassedThroughFromSource()
	{
		var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
		var source = new BufferSampleProvider(BuildTone(SampleRate), format);

		var provider = new SoundTouchSampleProvider(source);

		Assert.Equal(SampleRate, provider.WaveFormat.SampleRate);
		Assert.Equal(1, provider.WaveFormat.Channels);
	}

	[Theory]
	[InlineData(2.0)]
	[InlineData(0.5)]
	public void Tempo_ChangesSpeedWithoutChangingPitch(double tempo)
	{
		// The point of the time-stretcher over plain resampling: a 440 Hz tone played at
		// another speed must still be 440 Hz, not the chipmunk octave a resampler gives.
		var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
		var source = new BufferSampleProvider(BuildTone(SampleRate), format);

		float[] output = Drain(new SoundTouchSampleProvider(source) { Tempo = tempo });

		// A whole number of cycles at 220/440/880 Hz, taken from the middle so the
		// stretcher's start-up and tail-flush ramps are not measured.
		const int Window = 12000;
		Assert.True(output.Length >= Window, "Not enough output to measure the pitch.");
		var middle = output.AsSpan((output.Length - Window) / 2, Window);

		double atPitch = EnergyAt(middle, 440.0);
		Assert.True(atPitch > EnergyAt(middle, 880.0) * 4.0, "Output has drifted an octave up.");
		Assert.True(atPitch > EnergyAt(middle, 220.0) * 4.0, "Output has drifted an octave down.");
	}

	[Fact]
	public void Tempo_Normal_ProducesRoughlyTheSameLength()
	{
		int frames = SampleRate;
		var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
		var source = new BufferSampleProvider(BuildTone(frames), format);

		var provider = new SoundTouchSampleProvider(source) { Tempo = 1.0 };
		int outFrames = DrainFrameCount(provider, out float peak);

		// Allow a small tolerance for the time-stretch engine's internal latency.
		Assert.InRange(outFrames, frames - (frames / 10), frames + (frames / 10));
		Assert.True(peak > 0.01f, "Output should not be silent.");
	}

	[Fact]
	public void Tempo_Double_HalvesTheLength()
	{
		int frames = SampleRate;
		var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
		var source = new BufferSampleProvider(BuildTone(frames), format);

		var provider = new SoundTouchSampleProvider(source) { Tempo = 2.0 };
		int outFrames = DrainFrameCount(provider, out float peak);

		int expected = frames / 2;
		Assert.InRange(outFrames, expected - (frames / 10), expected + (frames / 10));
		Assert.True(peak > 0.01f, "Output should not be silent.");
	}

	[Fact]
	public void Tempo_Half_DoublesTheLength()
	{
		int frames = SampleRate;
		var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
		var source = new BufferSampleProvider(BuildTone(frames), format);

		var provider = new SoundTouchSampleProvider(source) { Tempo = 0.5 };
		int outFrames = DrainFrameCount(provider, out _);

		int expected = frames * 2;
		Assert.InRange(outFrames, expected - (frames / 5), expected + (frames / 5));
	}

	// Minimal in-memory ISampleProvider that emits a fixed buffer once, then signals
	// end of stream. Stands in for a decoded audio file in these tests.
	private sealed class BufferSampleProvider : ISampleProvider
	{
		private readonly float[] _data;
		private int _position;

		public BufferSampleProvider(float[] data, WaveFormat format)
		{
			_data = data;
			WaveFormat = format;
		}

		public WaveFormat WaveFormat { get; }

		public int Read(float[] buffer, int offset, int count)
		{
			int remaining = _data.Length - _position;
			int toCopy = Math.Min(count, remaining);
			if (toCopy <= 0)
				return 0;

			Array.Copy(_data, _position, buffer, offset, toCopy);
			_position += toCopy;
			return toCopy;
		}
	}
}
