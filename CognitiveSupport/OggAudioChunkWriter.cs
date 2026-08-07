using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace CognitiveSupport;

/// <summary>
/// Splits an audio file into standalone Ogg/Opus chunks.
///
/// The source is decoded once and re-encoded straight into the current chunk, so the
/// whole recording is never held in memory and each chunk gets its own container,
/// headers and timeline — every output file is independently playable and uploadable.
///
/// Encoder settings match <see cref="AudioFileConverter"/> and <see cref="AudioRecorder"/>
/// (48 kHz mono, 24 kbps VBR), so a chunk sounds like the recording it came from.
/// </summary>
public sealed class OggAudioChunkWriter : IAudioChunkWriter
{
	private const string ConvertedSourceFileName = "source.ogg";

	private const int SampleRate = 48000;

	// Opus requires 2.5, 5, 10, 20, 40 or 60ms frames. 20ms at 48kHz = 960 samples.
	private const int SamplesPerFrame = 960;
	private const int Bitrate = 24000;

	// Nominal payload is Bitrate/8 = 3000 bytes a second; the rest covers Ogg page
	// headers and the slack a VBR encoder takes on dense material. Deliberately
	// pessimistic — overestimating costs one extra chunk, underestimating costs a forced
	// cut in the middle of a word.
	private const double EncodedBytesPerSecondEstimate = Bitrate / 8.0 * 1.15;

	// The encoder hands the file whole Ogg pages, so bytes on disk trail the samples
	// already accepted by up to one page (65307 bytes at the format's maximum, and
	// Finish() can flush more than one). Closing a chunk this far short of the limit
	// leaves room for everything still to land. Capped at a quarter of the limit so an
	// unusually small one is still mostly usable rather than reserved away entirely.
	private const long PageFlushReserveBytes = 128 * 1024;

	// A chunk is never closed on the byte ceiling before it holds this much audio. Only
	// reachable with a limit smaller than the reserve above, and only there to stop a
	// nonsensical setting from producing one file per frame.
	private const long MinimumChunkSamples = SampleRate;

	/// <summary>
	/// Length of the audio in an Ogg/Opus file. This decodes the whole file, which on the
	/// multi-hour recordings this class exists for takes real time — hence the token.
	/// </summary>
	public static TimeSpan MeasureDuration(string audioFilePath, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(audioFilePath))
			throw new ArgumentException("Audio file path cannot be empty.", nameof(audioFilePath));

		long samples = 0;
		OggOpusPcmDecoder.DecodeToMonoPcm(audioFilePath, decoded =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			samples += decoded.Length;
		});

		return TimeSpan.FromSeconds(samples / (double)SampleRate);
	}

	public IReadOnlyList<string> WriteChunks(
		string audioFilePath,
		IReadOnlyList<SilenceRemovalPoint>? removalPoints,
		long maxChunkBytes,
		string outputDirectory,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(audioFilePath))
			throw new ArgumentException("Audio file path cannot be empty.", nameof(audioFilePath));
		if (string.IsNullOrWhiteSpace(outputDirectory))
			throw new ArgumentException("Output directory cannot be empty.", nameof(outputDirectory));

		Directory.CreateDirectory(outputDirectory);

		// An uploaded audio file is copied in as-is when silence stripping is off, so what
		// arrives here is not necessarily Ogg/Opus. Convert first — without trimming, so
		// the timeline the removal points were measured against is left alone.
		string source = audioFilePath;
		if (!OggOpusPcmDecoder.IsOggOpus(source))
		{
			string converted = Path.Combine(outputDirectory, ConvertedSourceFileName);
			AudioFileConverter.ConvertToOgg(source, converted, silenceOptions: null, cancellationToken);
			source = converted;
		}

		var ranges = AudioChunkPlanner.Plan(
			MeasureDuration(source, cancellationToken), maxChunkBytes, EncodedBytesPerSecondEstimate, removalPoints);

		return WritePlannedChunks(source, ranges, maxChunkBytes, outputDirectory, cancellationToken);
	}

	private static IReadOnlyList<string> WritePlannedChunks(
		string audioFilePath,
		IReadOnlyList<AudioChunkRange> ranges,
		long maxChunkBytes,
		string outputDirectory,
		CancellationToken cancellationToken)
	{
		var boundaries = ToBoundarySamples(ranges);
		long byteCeiling = maxChunkBytes > 0
			? Math.Max(maxChunkBytes - Math.Min(PageFlushReserveBytes, maxChunkBytes / 4), 1)
			: long.MaxValue;

		var paths = new List<string>();
		ChunkFile? current = null;
		long position = 0;      // samples accepted across every chunk so far
		int boundaryIndex = 0;

		try
		{
			var splitter = new PcmFrameSplitter(SamplesPerFrame);

			void WriteFrame(short[] frame)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (current is not null && ShouldCloseChunk(current, position, boundaries, boundaryIndex, byteCeiling))
				{
					current.Finish();
					current.Dispose();
					current = null;
				}

				// A cut forced by the byte ceiling can overtake planned boundaries; drop
				// the ones now behind us so they cannot fire a second, empty cut later.
				while (boundaryIndex < boundaries.Count && boundaries[boundaryIndex] <= position)
					boundaryIndex++;

				if (current is null)
				{
					current = ChunkFile.Create(outputDirectory, paths.Count);
					paths.Add(current.Path);
				}

				current.Write(frame);
				position += frame.Length;
			}

			OggOpusPcmDecoder.DecodeToMonoPcm(audioFilePath, decoded => splitter.AppendSamples(decoded, decoded.Length, WriteFrame));

			// Pad the final partial frame rather than dropping it, matching the converter:
			// the tail of the last chunk is the tail of the recording.
			splitter.Flush(WriteFrame, padWithSilence: true);

			current?.Finish();
		}
		finally
		{
			current?.Dispose();
		}

		return paths;
	}

	private static bool ShouldCloseChunk(
		ChunkFile current,
		long position,
		IReadOnlyList<long> boundaries,
		int boundaryIndex,
		long byteCeiling)
	{
		if (boundaryIndex < boundaries.Count && position >= boundaries[boundaryIndex])
			return true;

		return current.BytesWritten > byteCeiling && current.SamplesWritten >= MinimumChunkSamples;
	}

	/// <summary>
	/// Converts the planned ranges into the sample positions at which a new chunk starts.
	/// The final range's end is left out: it is the end of the recording, and a cut there
	/// would only ever open an empty trailing file.
	/// </summary>
	private static IReadOnlyList<long> ToBoundarySamples(IReadOnlyList<AudioChunkRange> ranges)
	{
		var boundaries = new List<long>();
		long previous = 0;

		for (int i = 0; i < ranges.Count - 1; i++)
		{
			long samples = (long)Math.Round(ranges[i].End.TotalSeconds * SampleRate);
			if (samples <= previous)
				continue;

			boundaries.Add(samples);
			previous = samples;
		}

		return boundaries;
	}

	/// <summary>
	/// One chunk file being written. Tracks its own size so the caller can stop short of
	/// the upload limit; the stream is opened unbuffered so <see cref="BytesWritten"/> is
	/// what is actually on disk rather than what is on its way there.
	/// </summary>
	private sealed class ChunkFile : IDisposable
	{
		private readonly FileStream _file;
		private readonly OpusOggWriteStream _ogg;
		private bool _finished;

		public string Path { get; }
		public long BytesWritten => _file.Position;
		public long SamplesWritten { get; private set; }

		private ChunkFile(string path, FileStream file, OpusOggWriteStream ogg)
		{
			Path = path;
			_file = file;
			_ogg = ogg;
		}

		public static ChunkFile Create(string outputDirectory, int index)
		{
			string path = System.IO.Path.Combine(
				outputDirectory,
				string.Format(CultureInfo.InvariantCulture, "chunk-{0:D3}.ogg", index + 1));

			var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1);
			try
			{
#pragma warning disable CS0618 // Concentus.OggFile 1.0.6 still requires the concrete OpusEncoder type.
				var encoder = new OpusEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP)
				{
					Bitrate = Bitrate,
					UseVBR = true,
					UseDTX = false // DTX is not supported in Ogg streams by Concentus.OggFile.
				};
#pragma warning restore CS0618

				return new ChunkFile(path, file, new OpusOggWriteStream(encoder, file, new OpusTags()));
			}
			catch
			{
				file.Dispose();
				throw;
			}
		}

		public void Write(short[] frame)
		{
			_ogg.WriteSamples(frame, 0, frame.Length);
			SamplesWritten += frame.Length;
		}

		public void Finish()
		{
			if (_finished) return;
			_finished = true;
			_ogg.Finish();
		}

		// OpusEncoder and OpusOggWriteStream are not IDisposable in Concentus; the file
		// stream is the only thing holding an OS handle.
		public void Dispose() => _file.Dispose();
	}
}
