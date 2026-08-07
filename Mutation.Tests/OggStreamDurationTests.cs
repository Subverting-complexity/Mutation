using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using CognitiveSupport;
using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;

namespace Mutation.Tests;

/// <summary>
/// Issue #290: splitting an oversized recording decoded it twice, the first pass only to
/// learn how long it was. The Ogg container states the length in every page header, so
/// the answer is two small reads away — and these tests hold that answer to the one the
/// decode gives, because a wrong length moves every chunk boundary.
/// </summary>
public class OggStreamDurationTests : IDisposable
{
	private const int SampleRate = 48000;
	private const int FrameSize = 960; // 20ms

	// RFC 3533 §6 page header layout, as far as this fixture needs it.
	private const int PageHeaderBytes = 27;
	private const int GranuleOffset = 6;
	private const int SegmentCountOffset = 26;

	private readonly string _workingDirectory = Directory.CreateTempSubdirectory("ogg-duration-tests").FullName;

	public void Dispose()
	{
		try { Directory.Delete(_workingDirectory, recursive: true); } catch { }
	}

	/// <summary>A 440Hz tone of the requested length as a single Ogg/Opus file.</summary>
	private string WriteFixture(string fileName, double seconds)
	{
		string path = Path.Combine(_workingDirectory, fileName);

		using (var outStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
		{
#pragma warning disable CS0618 // Concentus.OggFile 1.0.6 requires the concrete OpusEncoder type.
			var encoder = new OpusEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_AUDIO)
			{
				Bitrate = 32000,
				UseDTX = false
			};
#pragma warning restore CS0618
			var oggStream = new OpusOggWriteStream(encoder, outStream, new OpusTags());

			var frame = new short[FrameSize];
			for (int sample = 0; sample < FrameSize; sample++)
				frame[sample] = (short)(12000 * Math.Sin(2 * Math.PI * 440 * sample / SampleRate));

			for (int i = 0; i < seconds * SampleRate / FrameSize; i++)
				oggStream.WriteSamples(frame, 0, frame.Length);

			oggStream.Finish();
		}

		return path;
	}

	/// <summary>
	/// What the file decodes to, counted the long way round. This is the number the
	/// container reading has to agree with.
	/// </summary>
	private static double DecodedSeconds(string path)
	{
		long samples = 0;
		OggOpusPcmDecoder.DecodeToMonoPcm(path, decoded => samples += decoded.Length);
		return samples / (double)SampleRate;
	}

	/// <summary>
	/// Rewrites every page's granule position to -1 — the legal "no packet finishes on
	/// this page" value. The audio is untouched and still decodes; the container simply
	/// stops stating where it has got to, which is the condition the fallback exists for.
	/// </summary>
	private static void EraseGranulePositions(string path)
	{
		byte[] file = File.ReadAllBytes(path);
		int offset = 0;

		while (offset + PageHeaderBytes <= file.Length)
		{
			if (file[offset] != (byte)'O' || file[offset + 1] != (byte)'g'
				|| file[offset + 2] != (byte)'g' || file[offset + 3] != (byte)'S')
				break;

			BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(offset + GranuleOffset), ulong.MaxValue);

			int segmentCount = file[offset + SegmentCountOffset];
			int payload = 0;
			for (int i = 0; i < segmentCount; i++)
				payload += file[offset + PageHeaderBytes + i];

			offset += PageHeaderBytes + segmentCount + payload;
		}

		File.WriteAllBytes(path, file);
	}

	[Theory]
	[InlineData(1.0)]
	[InlineData(6.0)]
	[InlineData(12.5)]
	public void TryRead_AgreesWithTheDecodedDuration(double seconds)
	{
		string path = WriteFixture($"agree-{seconds}.ogg", seconds);

		TimeSpan? fromContainer = OggStreamDuration.TryRead(path);

		Assert.NotNull(fromContainer);
		// The container states the exact stream length; the decode can only ever land on
		// a whole 20ms frame boundary, so it may overshoot by up to one frame.
		Assert.InRange(fromContainer.Value.TotalSeconds, DecodedSeconds(path) - 0.02, DecodedSeconds(path));
	}

	// The whole point of the change: the fast path has to be the one that answers, or the
	// second decode is still there and nothing has been saved.
	[Fact]
	public void MeasureDuration_TakesTheContainerReading()
	{
		string path = WriteFixture("fast-path.ogg", 6);

		Assert.Equal(OggStreamDuration.TryRead(path), OggAudioChunkWriter.MeasureDuration(path));
	}

	[Fact]
	public void TryRead_GivesUp_WhenNoPageStatesAPosition()
	{
		string path = WriteFixture("no-granule.ogg", 6);
		EraseGranulePositions(path);

		Assert.Null(OggStreamDuration.TryRead(path));
	}

	// AC: a file whose trailing page cannot answer still measures correctly, by decoding.
	[Fact]
	public void MeasureDuration_FallsBackToDecoding_WhenTheContainerCannotAnswer()
	{
		string path = WriteFixture("fallback.ogg", 6);
		EraseGranulePositions(path);

		Assert.Equal(DecodedSeconds(path), OggAudioChunkWriter.MeasureDuration(path).TotalSeconds, 3);
	}

	// The fallback decodes the whole file, so the token still has to be honoured — the
	// cancellation #289 added must not have been quietly bypassed by the fast path.
	[Fact]
	public void MeasureDuration_GivesUpWhenCancelled_EvenOnTheFastPath()
	{
		string path = WriteFixture("cancelled-fast.ogg", 1);
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		Assert.ThrowsAny<OperationCanceledException>(() => OggAudioChunkWriter.MeasureDuration(path, cts.Token));
	}

	[Fact]
	public void TryRead_GivesUp_OnAFileThatIsNotOgg()
	{
		string path = Path.Combine(_workingDirectory, "not-ogg.bin");
		File.WriteAllBytes(path, new byte[4096]);

		Assert.Null(OggStreamDuration.TryRead(path));
	}

	[Fact]
	public void TryRead_GivesUp_OnAMissingFile()
	{
		Assert.Null(OggStreamDuration.TryRead(Path.Combine(_workingDirectory, "absent.ogg")));
	}

	// Each chunk the splitter writes is measured again to plan the upload, so the reading
	// has to hold for the files this app produces, not only for the source it was handed.
	[Fact]
	public void TryRead_AgreesWithTheDecodedDuration_ForEveryWrittenChunk()
	{
		string source = WriteFixture("chunked.ogg", 6);
		string outputDirectory = Path.Combine(_workingDirectory, "chunked-out");

		var chunks = new OggAudioChunkWriter().WriteChunks(
			source, Array.Empty<SilenceRemovalPoint>(), (long)(2 * 24000 / 8.0 * 1.15), outputDirectory, CancellationToken.None);

		Assert.True(chunks.Count > 1, "A 6s recording at a 2s limit has to be split.");
		foreach (string chunk in chunks)
		{
			TimeSpan? fromContainer = OggStreamDuration.TryRead(chunk);

			Assert.NotNull(fromContainer);
			double decoded = DecodedSeconds(chunk);
			Assert.InRange(fromContainer.Value.TotalSeconds, decoded - 0.02, decoded);
		}
	}
}
