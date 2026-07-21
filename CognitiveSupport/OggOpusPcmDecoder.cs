using Concentus;
using Concentus.Structs;
using System;
using System.IO;

namespace CognitiveSupport;

/// <summary>
/// Decodes an Ogg/Opus file into 48 kHz mono 16-bit PCM.
///
/// Windows Media Foundation has no Ogg/Opus demuxer, so these files (WhatsApp voice notes,
/// anything this app recorded itself) cannot be opened with <c>MediaFoundationReader</c> —
/// it throws MF_E_UNSUPPORTED_BYTESTREAM_TYPE (0xC00D36C4). Decoding happens in managed
/// code instead.
///
/// Two quirks of the bundled libraries are worked around here:
/// <list type="bullet">
/// <item>Concentus.Oggfile 1.0.6's reader is not used at all — on third-party files it stops
/// yielding audio partway through and then loops forever. <see cref="OggPacketReader"/>
/// demuxes instead.</item>
/// <item>Concentus rejects a whole multi-frame packet when any frame inside it is
/// zero-length (Opus DTX, which WhatsApp uses heavily). Packets are therefore split into
/// their frames and decoded one at a time, with DTX frames concealed rather than dropped —
/// otherwise roughly 7% of a typical voice note is lost and the timeline drifts.</item>
/// </list>
///
/// Opus always decodes at 48 kHz, so the only normalisation needed is the downmix to mono,
/// which matches the format <see cref="AudioFileConverter"/> encodes to.
/// </summary>
public static class OggOpusPcmDecoder
{
	public const int SampleRate = 48000;

	private static readonly byte[] OggMagic = "OggS"u8.ToArray();
	private static readonly byte[] OpusHeadMagic = "OpusHead"u8.ToArray();
	private static readonly byte[] OpusTagsMagic = "OpusTags"u8.ToArray();

	// The identification header lives in the first page of the logical stream, well within
	// this. Reading a single small block keeps the format sniff cheap.
	private const int HeaderScanBytes = 4096;

	// 120ms at 48kHz — the longest frame Opus can carry.
	private const int MaxFrameSamples = 5760;

	// Concentus decodes mono and stereo only; it rejects anything else outright. Surround
	// Opus (RFC 7845 channel mapping family 1) therefore cannot be handled here.
	private const int MaxDecodableChannels = 2;

	/// <summary>
	/// True when the file is an Ogg stream carrying Opus that this decoder can actually handle.
	/// Detection is by content rather than extension so a mis-named file still decodes, and so
	/// Ogg/Vorbis — which Concentus cannot decode — is correctly reported as not-Opus and left
	/// to the caller's fallback path. Surround Opus is reported as not-decodable for the same
	/// reason: better to fall through to the caller's fallback, which explains what to do, than
	/// to fail deep inside the codec with a bare "Number of channels must be 1 or 2".
	/// </summary>
	public static bool IsOggOpus(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
			return false;

		try
		{
			using var stream = File.OpenRead(filePath);
			return TryReadOpusHead(stream, out int channels, out _) && channels <= MaxDecodableChannels;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>
	/// Reads the channel count from the file's OpusHead identification header.
	/// </summary>
	/// <exception cref="InvalidDataException">The file is not an Ogg/Opus stream.</exception>
	public static int ReadChannelCount(string filePath)
	{
		using var stream = File.OpenRead(filePath);
		if (!TryReadOpusHead(stream, out int channels, out _))
			throw new InvalidDataException($"'{Path.GetFileName(filePath)}' is not an Ogg/Opus stream.");

		return channels;
	}

	/// <summary>
	/// Streams the file's audio to <paramref name="onSamples"/> as 48 kHz mono 16-bit PCM,
	/// one decoded frame at a time. Stereo sources are averaged down to mono. The array handed
	/// to the callback is freshly allocated per frame, so the callback may retain it.
	/// </summary>
	/// <exception cref="InvalidDataException">The file is not an Ogg/Opus stream.</exception>
	/// <exception cref="NotSupportedException">The file is surround Opus, which Concentus
	/// cannot decode.</exception>
	public static void DecodeToMonoPcm(string filePath, Action<short[]> onSamples)
	{
		if (string.IsNullOrWhiteSpace(filePath))
			throw new ArgumentException("File path cannot be empty", nameof(filePath));
		if (onSamples is null)
			throw new ArgumentNullException(nameof(onSamples));

		int channels;
		int preSkipSamples;
		using (var headerStream = File.OpenRead(filePath))
		{
			if (!TryReadOpusHead(headerStream, out channels, out preSkipSamples))
				throw new InvalidDataException($"'{Path.GetFileName(filePath)}' is not an Ogg/Opus stream.");
		}

		// Say so plainly rather than letting Concentus surface a bare "Number of channels must
		// be 1 or 2" from somewhere inside the codec.
		if (channels > MaxDecodableChannels)
			throw new NotSupportedException(
				$"'{Path.GetFileName(filePath)}' is {channels}-channel Ogg/Opus, which cannot be decoded. " +
				"Convert it to mono or stereo first.");

		var decoder = OpusCodecFactory.CreateDecoder(SampleRate, channels);
		var buffer = new short[MaxFrameSamples * channels];
		int remainingPreSkip = preSkipSamples;

		using var stream = File.OpenRead(filePath);
		foreach (byte[] packet in OggPacketReader.ReadPackets(stream))
		{
			if (IsHeaderPacket(packet))
				continue;

			DecodePacket(decoder, packet, channels, buffer, ref remainingPreSkip, onSamples);
		}
	}

	/// <summary>
	/// Decodes one Opus packet frame by frame. Splitting is what makes DTX-bearing packets
	/// decodable: each frame is re-wrapped as a single-frame (code 0) packet, and a
	/// zero-length frame is handed to the decoder as a loss so it emits concealment audio of
	/// the right length instead of the packet failing outright.
	/// </summary>
	private static void DecodePacket(
		IOpusDecoder decoder,
		byte[] packet,
		int channels,
		short[] buffer,
		ref int remainingPreSkip,
		Action<short[]> onSamples)
	{
		// Concentus.Oggfile's writer terminates a stream with an empty packet, and its parser
		// overflows rather than rejecting one, so drop empties before parsing.
		if (packet.Length == 0)
			return;

		OpusPacketInfo info;
		try
		{
			info = OpusPacketInfo.ParseOpusPacket(packet.AsSpan());
		}
		catch (Exception ex) when (ex is OpusException or OverflowException or ArgumentException)
		{
			return; // Unparseable packet — skip it rather than abandoning the rest of the file.
		}

		int frameSamples = info.NumSamplesPerFrame(SampleRate);
		if (frameSamples <= 0 || frameSamples > MaxFrameSamples)
			return;

		byte singleFrameToc = (byte)(info.TOCByte & 0xFC); // clear the frame-count code
		var singleFramePacket = new byte[1];

		foreach (byte[] frame in info.Frames)
		{
			int decodedSamples;
			try
			{
				if (frame.Length == 0)
				{
					// DTX: no data was transmitted for this frame. Ask for concealment so the
					// output keeps the same duration as the source.
					decodedSamples = decoder.Decode(ReadOnlySpan<byte>.Empty, buffer.AsSpan(0, frameSamples * channels), frameSamples, false);
				}
				else
				{
					if (singleFramePacket.Length != frame.Length + 1)
						singleFramePacket = new byte[frame.Length + 1];

					singleFramePacket[0] = singleFrameToc;
					Buffer.BlockCopy(frame, 0, singleFramePacket, 1, frame.Length);
					decodedSamples = decoder.Decode(singleFramePacket, buffer.AsSpan(0, frameSamples * channels), frameSamples, false);
				}
			}
			catch (OpusException)
			{
				// A frame the decoder cannot handle becomes silence of the same length, so a
				// single bad frame costs a few milliseconds instead of the whole recording.
				Array.Clear(buffer, 0, frameSamples * channels);
				decodedSamples = frameSamples;
			}

			if (decodedSamples <= 0)
				continue;

			EmitSamples(buffer, decodedSamples, channels, ref remainingPreSkip, onSamples);
		}
	}

	/// <summary>
	/// Downmixes to mono if needed, drops the encoder's pre-skip priming samples, and hands
	/// the result to the caller.
	/// </summary>
	private static void EmitSamples(
		short[] buffer,
		int decodedSamples,
		int channels,
		ref int remainingPreSkip,
		Action<short[]> onSamples)
	{
		int start = 0;
		if (remainingPreSkip > 0)
		{
			int skipped = Math.Min(remainingPreSkip, decodedSamples);
			remainingPreSkip -= skipped;
			start = skipped;
			if (start >= decodedSamples)
				return;
		}

		int count = decodedSamples - start;
		var mono = new short[count];

		if (channels == 1)
		{
			Array.Copy(buffer, start, mono, 0, count);
		}
		else
		{
			// Averaging in int avoids the overflow a short-domain sum would hit on loud material.
			for (int i = 0; i < count; i++)
			{
				int sum = 0;
				int offset = (start + i) * channels;
				for (int channel = 0; channel < channels; channel++)
					sum += buffer[offset + channel];

				mono[i] = (short)(sum / channels);
			}
		}

		onSamples(mono);
	}

	private static bool IsHeaderPacket(byte[] packet) =>
		MatchesAt(packet, 0, OpusHeadMagic) || MatchesAt(packet, 0, OpusTagsMagic);

	/// <summary>
	/// Locates the OpusHead identification header near the start of the stream and reads the
	/// channel count (byte +9) and pre-skip sample count (bytes +10..11), per RFC 7845 §5.1.
	/// </summary>
	private static bool TryReadOpusHead(Stream stream, out int channels, out int preSkipSamples)
	{
		channels = 0;
		preSkipSamples = 0;

		var buffer = new byte[HeaderScanBytes];
		int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
		if (read < OggMagic.Length || !MatchesAt(buffer, 0, OggMagic, read))
			return false;

		int headIndex = IndexOf(buffer, read, OpusHeadMagic);
		if (headIndex < 0)
			return false;

		int channelIndex = headIndex + OpusHeadMagic.Length + 1; // skip the version byte
		if (channelIndex + 2 >= read)
			return false;

		channels = buffer[channelIndex];
		preSkipSamples = buffer[channelIndex + 1] | (buffer[channelIndex + 2] << 8);
		return channels > 0;
	}

	private static int IndexOf(byte[] haystack, int length, byte[] needle)
	{
		for (int i = 0; i + needle.Length <= length; i++)
		{
			if (MatchesAt(haystack, i, needle))
				return i;
		}

		return -1;
	}

	private static bool MatchesAt(byte[] haystack, int offset, byte[] needle) =>
		MatchesAt(haystack, offset, needle, haystack.Length);

	private static bool MatchesAt(byte[] haystack, int offset, byte[] needle, int length)
	{
		if (offset + needle.Length > length)
			return false;

		for (int i = 0; i < needle.Length; i++)
		{
			if (haystack[offset + i] != needle[i])
				return false;
		}

		return true;
	}
}
