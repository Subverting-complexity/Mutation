using System;
using System.Buffers.Binary;
using System.IO;

namespace CognitiveSupport;

/// <summary>
/// Reads how long an Ogg/Opus file plays for out of its container, without decoding a
/// single sample.
///
/// RFC 7845 §4 puts a granule position — the count of 48 kHz samples the stream has
/// produced up to and including that page — in every Ogg page header. The final page of
/// the audio stream therefore already states the length, and the <c>OpusHead</c>
/// pre-skip (§5.1) is the priming the decoder throws away. Length is the one minus the
/// other, from two small reads rather than a pass over the whole file.
///
/// This exists because splitting an oversized recording used to decode it twice — once
/// only to learn its length — and that only happens on files big enough to be worth
/// hours of audio (issue #290). Everything here is best-effort: an unreadable or
/// unconvincing tail returns null so the caller can fall back to decoding.
/// </summary>
public static class OggStreamDuration
{
	// RFC 3533 §6: capture pattern, version, header type, granule position, serial.
	private const int PageHeaderBytes = 27;
	private const int VersionOffset = 4;
	private const int HeaderTypeOffset = 5;
	private const int GranuleOffset = 6;
	private const int SerialNumberOffset = 14;
	private const int SegmentCountOffset = 26;

	// Only the three lowest bits of the header type are defined (continued, BOS, EOS).
	// Anything else set means these bytes are not a page header.
	private const int DefinedHeaderTypeBits = 0x07;

	// An Ogg page is at most 27 + 255 + 255*255 = 65307 bytes, so a tail this size always
	// contains the whole of the last page however large it is.
	private const int TailScanBytes = 128 * 1024;

	// OpusHead has to be alone in the first page of its logical stream, so it is found
	// immediately unless the container multiplexes several streams ahead of the audio.
	// A cap keeps a file that is not really Ogg from being walked end to end.
	private const int MaxPagesScannedForOpusHead = 64;

	private static readonly byte[] OggMagic = "OggS"u8.ToArray();
	private static readonly byte[] OpusHeadMagic = "OpusHead"u8.ToArray();

	/// <summary>
	/// Length of the Opus stream in <paramref name="audioFilePath"/>, or null when the
	/// container cannot be trusted to answer — no readable <c>OpusHead</c>, no final page
	/// carrying a granule position, or a granule that does not exceed the pre-skip.
	/// A null is an instruction to measure some other way, not a claim the file is broken.
	/// </summary>
	public static TimeSpan? TryRead(string audioFilePath)
	{
		if (string.IsNullOrWhiteSpace(audioFilePath))
			return null;

		try
		{
			using var stream = File.OpenRead(audioFilePath);

			if (!TryReadOpusHeadPage(stream, out uint serialNumber, out int preSkipSamples))
				return null;

			if (!TryReadFinalGranule(stream, serialNumber, out long granule))
				return null;

			long samples = granule - preSkipSamples;
			return samples > 0
				? TimeSpan.FromSeconds(samples / (double)OggOpusPcmDecoder.SampleRate)
				: null;
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	/// <summary>
	/// Walks pages from the start until one begins with an <c>OpusHead</c> packet, and
	/// reads the logical stream it belongs to along with its pre-skip. Walking rather
	/// than scanning raw bytes is what keeps an "OpusHead" that happens to sit inside a
	/// comment string from being mistaken for the identification header.
	/// </summary>
	private static bool TryReadOpusHeadPage(Stream stream, out uint serialNumber, out int preSkipSamples)
	{
		serialNumber = 0;
		preSkipSamples = 0;

		var header = new byte[PageHeaderBytes];

		for (int page = 0; page < MaxPagesScannedForOpusHead; page++)
		{
			if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
				return false;
			if (!IsPageHeader(header, 0))
				return false;

			int segmentCount = header[SegmentCountOffset];
			var segmentTable = new byte[segmentCount];
			if (stream.ReadAtLeast(segmentTable, segmentCount, throwOnEndOfStream: false) < segmentCount)
				return false;

			int payloadBytes = 0;
			foreach (byte segment in segmentTable)
				payloadBytes += segment;

			var payload = new byte[payloadBytes];
			if (stream.ReadAtLeast(payload, payloadBytes, throwOnEndOfStream: false) < payloadBytes)
				return false;

			// RFC 7845 §5.1: "OpusHead", version, channel count, then the 16-bit pre-skip.
			if (StartsWith(payload, OpusHeadMagic) && payload.Length >= OpusHeadMagic.Length + 4)
			{
				serialNumber = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(SerialNumberOffset));
				preSkipSamples = BinaryPrimitives.ReadUInt16LittleEndian(
					payload.AsSpan(OpusHeadMagic.Length + 2));
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Finds the last page of the given logical stream by scanning the tail of the file
	/// backwards, and reads its granule position. Scanning back from the end rather than
	/// forward from the start is the whole point: it costs one small read regardless of
	/// how many hours of audio sit in between.
	/// </summary>
	private static bool TryReadFinalGranule(Stream stream, uint serialNumber, out long granule)
	{
		granule = 0;

		int tailBytes = (int)Math.Min(stream.Length, TailScanBytes);
		if (tailBytes < PageHeaderBytes)
			return false;

		var tail = new byte[tailBytes];
		stream.Position = stream.Length - tailBytes;
		if (stream.ReadAtLeast(tail, tailBytes, throwOnEndOfStream: false) < tailBytes)
			return false;

		for (int offset = tailBytes - PageHeaderBytes; offset >= 0; offset--)
		{
			if (!IsPageHeader(tail, offset))
				continue;
			if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(offset + SerialNumberOffset)) != serialNumber)
				continue;

			ulong stated = BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(offset + GranuleOffset));

			// -1 means no packet finishes on this page, so it states no position at all,
			// and 0 is what the header pages carry. Keep walking back to a page that
			// actually reports progress. Anything past long.MaxValue is not a sample
			// count, it is a misread.
			if (stated == 0 || stated > long.MaxValue)
				continue;

			granule = (long)stated;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Whether the bytes at <paramref name="offset"/> plausibly start a page header:
	/// the capture pattern, the only stream structure version there has ever been, and
	/// no undefined header-type bits. Cheap checks, but enough that a stray "OggS" in
	/// compressed audio is very unlikely to be read as the end of the recording.
	/// </summary>
	private static bool IsPageHeader(byte[] buffer, int offset)
	{
		if (offset + PageHeaderBytes > buffer.Length)
			return false;

		for (int i = 0; i < OggMagic.Length; i++)
		{
			if (buffer[offset + i] != OggMagic[i])
				return false;
		}

		return buffer[offset + VersionOffset] == 0
			&& (buffer[offset + HeaderTypeOffset] & ~DefinedHeaderTypeBits) == 0;
	}

	private static bool StartsWith(byte[] buffer, byte[] prefix)
	{
		if (buffer.Length < prefix.Length)
			return false;

		for (int i = 0; i < prefix.Length; i++)
		{
			if (buffer[i] != prefix[i])
				return false;
		}

		return true;
	}
}
