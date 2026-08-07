using System;

namespace Mutation.Ui.Core;

/// <summary>Peak and RMS amplitude over one render window, both in 0..1.</summary>
internal readonly record struct WaveformLevels(double Peak, double Rms);

/// <summary>
/// The capture-buffer → render-window → level pipeline behind the microphone waveform.
/// <para>
/// Samples arrive from the capture device on its own thread and land in a ring buffer;
/// a render tick on the UI thread takes a snapshot of the last window's worth, oldest
/// sample first, and measures it. Splitting this out of
/// <c>MicrophoneVisualizationController</c> lets the wrap-around, the partially-filled
/// first window, and the level maths be tested directly — none of which need a
/// microphone, a plot, or a dispatcher, and all of which are silently wrong rather than
/// noisy when they break.
/// </para>
/// </summary>
internal sealed class WaveformSampleBuffer
{
	private readonly object _lock = new();
	private readonly double[] _ring;
	private int _writeIndex;
	private bool _wrapped;

	/// <param name="windowSampleCount">How many samples one rendered window holds. Must be positive.</param>
	public WaveformSampleBuffer(int windowSampleCount)
	{
		if (windowSampleCount <= 0)
			throw new ArgumentOutOfRangeException(nameof(windowSampleCount), windowSampleCount, "A waveform window needs at least one sample.");

		_ring = new double[windowSampleCount];
		RenderBuffer = new double[windowSampleCount];
	}

	/// <summary>
	/// The array <see cref="Snapshot"/> writes into, oldest sample first. Handed to the plot
	/// once and then refilled in place, because ScottPlot's Signal holds the reference it was
	/// given — replacing the array would leave the plot drawing a frozen copy.
	/// </summary>
	public double[] RenderBuffer { get; }

	/// <summary>How many samples one window holds.</summary>
	public int WindowSampleCount => _ring.Length;

	/// <summary>Drops everything captured so far and flattens the rendered window.</summary>
	public void Reset()
	{
		lock (_lock)
		{
			Array.Clear(_ring, 0, _ring.Length);
			Array.Clear(RenderBuffer, 0, RenderBuffer.Length);
			_writeIndex = 0;
			_wrapped = false;
		}
	}

	/// <summary>
	/// Appends 16-bit little-endian mono PCM from a capture callback. Reads
	/// <paramref name="byteCount"/> bytes from the front of <paramref name="pcm16"/> —
	/// capture buffers are reused and only partly filled, so the array's own length says
	/// nothing about how much of it is audio. A trailing odd byte is ignored.
	/// </summary>
	/// <returns>How many samples were taken.</returns>
	public int Write(byte[] pcm16, int byteCount)
	{
		if (pcm16 is null) throw new ArgumentNullException(nameof(pcm16));
		if (byteCount <= 0)
			return 0;

		int sampleCount = Math.Min(byteCount, pcm16.Length) / 2;
		if (sampleCount <= 0)
			return 0;

		lock (_lock)
		{
			for (int i = 0; i < sampleCount; i++)
			{
				short sample = BitConverter.ToInt16(pcm16, i * 2);
				_ring[_writeIndex++] = sample / 32768d;
				if (_writeIndex >= _ring.Length)
				{
					_writeIndex = 0;
					_wrapped = true;
				}
			}
		}

		return sampleCount;
	}

	/// <summary>
	/// Fills <see cref="RenderBuffer"/> with the most recent window, oldest sample first.
	/// Before a full window has been captured the valid samples sit at the end and the lead
	/// is zeroed, so the trace grows in from the right rather than jumping.
	/// </summary>
	/// <returns>How many of the samples in <see cref="RenderBuffer"/> are real audio.</returns>
	public int Snapshot()
	{
		lock (_lock)
		{
			if (!_wrapped && _writeIndex == 0)
			{
				Array.Clear(RenderBuffer, 0, RenderBuffer.Length);
				return 0;
			}

			int length = RenderBuffer.Length;

			if (_wrapped)
			{
				// The oldest sample is the one about to be overwritten, so the window is the
				// tail from the write cursor followed by the head before it.
				int tailLength = length - _writeIndex;
				if (tailLength > 0)
					Array.Copy(_ring, _writeIndex, RenderBuffer, 0, tailLength);
				if (_writeIndex > 0)
					Array.Copy(_ring, 0, RenderBuffer, tailLength, _writeIndex);
				return length;
			}

			int validCount = _writeIndex;
			int leadingZeros = length - validCount;
			if (leadingZeros > 0)
				Array.Clear(RenderBuffer, 0, leadingZeros);
			Array.Copy(_ring, 0, RenderBuffer, leadingZeros, validCount);
			return validCount;
		}
	}

	/// <summary>
	/// Measures the newest <paramref name="validSamples"/> samples of a snapshot. Only the
	/// real audio at the end of the window is measured, so the zero-padded lead of a
	/// partially-filled first window does not drag the level down towards silence.
	/// </summary>
	public static WaveformLevels MeasureLevels(double[] window, int validSamples)
	{
		if (window is null) throw new ArgumentNullException(nameof(window));
		if (validSamples <= 0 || window.Length == 0)
			return new WaveformLevels(0, 0);

		int samplesToProcess = Math.Min(validSamples, window.Length);
		int startIndex = window.Length - samplesToProcess;

		double peak = 0;
		double sumSquares = 0;
		for (int i = startIndex; i < window.Length; i++)
		{
			double value = window[i];
			double abs = Math.Abs(value);
			if (abs > peak)
				peak = abs;
			sumSquares += value * value;
		}

		return new WaveformLevels(peak, Math.Sqrt(sumSquares / samplesToProcess));
	}
}
