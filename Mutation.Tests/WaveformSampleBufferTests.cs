using System;
using System.Threading.Tasks;
using Mutation.Ui.Core;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="WaveformSampleBuffer"/> — the capture-buffer → render-window → level
/// pipeline that drives the microphone waveform and the level meter (issue #255).
/// <para>
/// <see cref="WaveformRenderGate"/> decides <em>whether</em> a frame is drawn and has its
/// own tests; this is what actually gets drawn, and it had none. A wrap-around copied in
/// the wrong order draws a trace that jumps backwards; a level measured across the
/// zero-padded lead of the first window reads far quieter than the room is. Neither throws,
/// and the level meter is one of the few pieces of feedback here that a screen reader
/// cannot narrate — so a wrong number is simply believed.
/// </para>
/// </summary>
public class WaveformSampleBufferTests
{
	// 16-bit little-endian mono PCM, the format the capture device is opened with.
	private static byte[] Pcm(params short[] samples)
	{
		var bytes = new byte[samples.Length * 2];
		for (int i = 0; i < samples.Length; i++)
			BitConverter.GetBytes(samples[i]).CopyTo(bytes, i * 2);
		return bytes;
	}

	private const short FullScale = 16384; // 0.5 once scaled by 1/32768.

	[Fact]
	public void A_window_must_hold_at_least_one_sample()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new WaveformSampleBuffer(0));
	}

	[Fact]
	public void A_buffer_nothing_has_been_written_to_reports_no_valid_samples()
	{
		var buffer = new WaveformSampleBuffer(4);

		Assert.Equal(0, buffer.Snapshot());
		Assert.Equal(new double[4], buffer.RenderBuffer);
	}

	[Fact]
	public void The_render_buffer_is_the_same_array_every_time()
	{
		var buffer = new WaveformSampleBuffer(4);
		double[] handedToThePlot = buffer.RenderBuffer;

		buffer.Write(Pcm(1, 2, 3, 4), 8);
		buffer.Snapshot();

		// ScottPlot's Signal keeps the array it was given at Initialize. Handing back a new
		// one would leave the plot redrawing a frozen copy for the rest of the session.
		Assert.Same(handedToThePlot, buffer.RenderBuffer);
	}

	[Fact]
	public void A_partly_filled_first_window_puts_the_audio_at_the_end_behind_silence()
	{
		var buffer = new WaveformSampleBuffer(4);

		buffer.Write(Pcm(FullScale, FullScale), 4);
		int valid = buffer.Snapshot();

		Assert.Equal(2, valid);
		Assert.Equal(new[] { 0d, 0d, 0.5d, 0.5d }, buffer.RenderBuffer);
	}

	[Fact]
	public void A_full_window_reads_oldest_sample_first()
	{
		var buffer = new WaveformSampleBuffer(4);

		buffer.Write(Pcm(1024, 2048, 4096, 8192), 8);
		int valid = buffer.Snapshot();

		Assert.Equal(4, valid);
		Assert.Equal(
			new[] { 1024 / 32768d, 2048 / 32768d, 4096 / 32768d, 8192 / 32768d },
			buffer.RenderBuffer);
	}

	[Fact]
	public void Once_the_ring_wraps_the_window_is_the_last_N_samples_still_in_order()
	{
		var buffer = new WaveformSampleBuffer(4);

		buffer.Write(Pcm(1, 2, 3, 4, 5, 6), 12);
		int valid = buffer.Snapshot();

		Assert.Equal(4, valid);
		// 1 and 2 have been overwritten; what is left has to read forwards in time, not from
		// wherever the write cursor happens to sit.
		Assert.Equal(new[] { 3 / 32768d, 4 / 32768d, 5 / 32768d, 6 / 32768d }, buffer.RenderBuffer);
	}

	[Fact]
	public void A_write_that_lands_exactly_on_the_end_of_the_ring_still_reads_in_order()
	{
		var buffer = new WaveformSampleBuffer(4);
		buffer.Write(Pcm(1, 2, 3, 4), 8);

		buffer.Write(Pcm(5, 6, 7, 8), 8);

		// The write cursor is back at zero and the ring is full — the case where an
		// off-by-one splits the window in the wrong place.
		Assert.Equal(4, buffer.Snapshot());
		Assert.Equal(new[] { 5 / 32768d, 6 / 32768d, 7 / 32768d, 8 / 32768d }, buffer.RenderBuffer);
	}

	[Fact]
	public void Snapshot_can_be_taken_repeatedly_without_consuming_anything()
	{
		var buffer = new WaveformSampleBuffer(4);
		buffer.Write(Pcm(1, 2, 3, 4, 5), 10);

		buffer.Snapshot();
		double[] first = (double[])buffer.RenderBuffer.Clone();
		buffer.Snapshot();

		Assert.Equal(first, buffer.RenderBuffer);
	}

	[Fact]
	public void Only_the_bytes_the_device_actually_filled_are_read()
	{
		var buffer = new WaveformSampleBuffer(4);

		// NAudio hands back a reused buffer with BytesRecorded saying how much is audio; the
		// rest is the previous callback's samples. Reading past it replays old audio.
		byte[] deviceBuffer = Pcm(FullScale, FullScale, 9999, 9999);
		buffer.Write(deviceBuffer, 4);

		Assert.Equal(2, buffer.Snapshot());
		Assert.Equal(new[] { 0d, 0d, 0.5d, 0.5d }, buffer.RenderBuffer);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-4)]
	public void A_callback_carrying_no_audio_is_ignored(int byteCount)
	{
		var buffer = new WaveformSampleBuffer(4);

		Assert.Equal(0, buffer.Write(Pcm(1, 2), byteCount));
		Assert.Equal(0, buffer.Snapshot());
	}

	[Fact]
	public void A_trailing_odd_byte_is_dropped_rather_than_read_as_half_a_sample()
	{
		var buffer = new WaveformSampleBuffer(4);

		Assert.Equal(1, buffer.Write(Pcm(FullScale, 0), 3));
		Assert.Equal(1, buffer.Snapshot());
	}

	[Fact]
	public void A_byte_count_larger_than_the_buffer_reads_only_what_is_there()
	{
		var buffer = new WaveformSampleBuffer(4);

		Assert.Equal(2, buffer.Write(Pcm(FullScale, FullScale), 999));
	}

	[Fact]
	public void Reset_drops_the_captured_audio_and_flattens_the_window()
	{
		var buffer = new WaveformSampleBuffer(4);
		buffer.Write(Pcm(FullScale, FullScale, FullScale, FullScale), 8);
		buffer.Snapshot();

		buffer.Reset();

		Assert.Equal(new double[4], buffer.RenderBuffer);
		Assert.Equal(0, buffer.Snapshot());
	}

	[Fact]
	public void After_a_reset_the_window_fills_from_empty_again()
	{
		var buffer = new WaveformSampleBuffer(4);
		buffer.Write(Pcm(1, 2, 3, 4, 5, 6), 12);

		buffer.Reset();
		buffer.Write(Pcm(FullScale), 2);

		Assert.Equal(1, buffer.Snapshot());
		Assert.Equal(new[] { 0d, 0d, 0d, 0.5d }, buffer.RenderBuffer);
	}

	[Fact]
	public void MeasureLevels_reports_zero_for_a_window_with_no_audio_in_it()
	{
		var levels = WaveformSampleBuffer.MeasureLevels(new double[4], validSamples: 0);

		Assert.Equal(0, levels.Peak);
		Assert.Equal(0, levels.Rms);
	}

	[Fact]
	public void MeasureLevels_reports_the_loudest_sample_regardless_of_sign()
	{
		var levels = WaveformSampleBuffer.MeasureLevels(new[] { 0.1, -0.8, 0.3, 0.2 }, validSamples: 4);

		Assert.Equal(0.8, levels.Peak, 10);
	}

	[Fact]
	public void MeasureLevels_reports_the_root_mean_square_of_the_window()
	{
		var levels = WaveformSampleBuffer.MeasureLevels(new[] { 0.5, -0.5, 0.5, -0.5 }, validSamples: 4);

		Assert.Equal(0.5, levels.Rms, 10);
	}

	[Fact]
	public void MeasureLevels_ignores_the_silent_lead_of_a_partly_filled_window()
	{
		// The first two entries are padding, not a quiet room. Averaging them in halves the
		// reported level and the meter under-reads for the first frames of every capture.
		var levels = WaveformSampleBuffer.MeasureLevels(new[] { 0d, 0d, 0.5, 0.5 }, validSamples: 2);

		Assert.Equal(0.5, levels.Rms, 10);
		Assert.Equal(0.5, levels.Peak, 10);
	}

	[Fact]
	public void MeasureLevels_survives_a_count_larger_than_the_window()
	{
		var levels = WaveformSampleBuffer.MeasureLevels(new[] { 0.5, 0.5 }, validSamples: 99);

		Assert.Equal(0.5, levels.Rms, 10);
	}

	[Fact]
	public void A_full_scale_window_measures_at_the_top_of_the_meter()
	{
		var buffer = new WaveformSampleBuffer(4);
		buffer.Write(Pcm(short.MinValue, short.MinValue, short.MinValue, short.MinValue), 8);

		var levels = WaveformSampleBuffer.MeasureLevels(buffer.RenderBuffer, buffer.Snapshot());

		// short.MinValue / 32768 is exactly -1: the meter clamps at 1, so anything above that
		// would be a scaling mistake rather than a loud room.
		Assert.Equal(1.0, levels.Peak, 10);
		Assert.Equal(1.0, levels.Rms, 10);
	}

	[Fact]
	public async Task Capture_writes_and_render_snapshots_can_run_at_the_same_time()
	{
		// Writes arrive on the capture device's thread while snapshots are taken on the UI
		// thread's frame timer. A torn read here would be a garbled trace, not a crash.
		var buffer = new WaveformSampleBuffer(512);
		using var cancellation = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));

		var writer = Task.Run(() =>
		{
			var chunk = Pcm(new short[64]);
			for (int i = 0; i < 2_000 && !cancellation.IsCancellationRequested; i++)
				buffer.Write(chunk, chunk.Length);
		});

		var reader = Task.Run(() =>
		{
			for (int i = 0; i < 2_000 && !cancellation.IsCancellationRequested; i++)
				Assert.InRange(buffer.Snapshot(), 0, buffer.WindowSampleCount);
		});

		await Task.WhenAll(writer, reader);
	}
}
