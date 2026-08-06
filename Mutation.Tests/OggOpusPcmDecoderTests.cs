using System;
using System.IO;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Container- and packet-level regressions for the Ogg/Opus decode path. The fixtures are
/// hand-built because the bundled encoder cannot produce the packet shapes that broke
/// importing real-world files (DTX frames, trailing empty packets).
/// </summary>
public class OggOpusPcmDecoderTests : IDisposable
{
	private const int SampleRate = 48000;

	// Config 13 (hybrid, super-wideband, 20ms), mono, code 3 (arbitrary frame count).
	private const byte HybridSwb20MsMonoCode3 = 0x6B;

	// v=0 (CBR), p=0, M=6 frames. With no payload every frame is zero-length: pure DTX.
	private const byte SixCbrFrames = 0x06;

	private readonly string _workingDirectory = Directory.CreateTempSubdirectory("ogg-decoder-tests").FullName;

	public void Dispose()
	{
		try { Directory.Delete(_workingDirectory, recursive: true); } catch { }
	}

	private string WriteFixture(string fileName, int channels, int preSkip, params byte[][] audioPackets)
	{
		string path = Path.Combine(_workingDirectory, fileName);

		using var stream = File.Create(path);
		OggStreamBuilder.WritePage(stream, new[] { OggStreamBuilder.OpusHead(channels, preSkip) });
		OggStreamBuilder.WritePage(stream, new[] { OggStreamBuilder.OpusTags() }, pageSequence: 1);

		int page = 2;
		foreach (byte[] packet in audioPackets)
			OggStreamBuilder.WritePage(stream, OggStreamBuilder.ToSegments(packet), pageSequence: page++);

		return path;
	}

	private static int CountSamples(string path)
	{
		int total = 0;
		OggOpusPcmDecoder.DecodeToMonoPcm(path, samples => total += samples.Length);
		return total;
	}

	[Fact]
	public void DtxFrames_AreConcealedRatherThanDroppingThePacket()
	{
		// A packet whose frames are all zero-length. Concentus rejects it as a whole packet,
		// which is why real WhatsApp voice notes lost ~7% of their audio and drifted out of
		// sync before the frame-by-frame path existed.
		string path = WriteFixture("dtx.ogg", channels: 1, preSkip: 0,
			new[] { HybridSwb20MsMonoCode3, SixCbrFrames });

		// 6 frames x 20ms at 48kHz.
		Assert.Equal(6 * 960, CountSamples(path));
	}

	[Fact]
	public void PreSkipSamples_AreDropped()
	{
		const int preSkip = 312; // the usual encoder priming amount
		string path = WriteFixture("preskip.ogg", channels: 1, preSkip: preSkip,
			new[] { HybridSwb20MsMonoCode3, SixCbrFrames });

		Assert.Equal(6 * 960 - preSkip, CountSamples(path));
	}

	[Fact]
	public void TrailingEmptyPacket_IsIgnored()
	{
		// Concentus.Oggfile's writer ends every stream it produces with an empty packet, and
		// its own packet parser overflows on one.
		string path = Path.Combine(_workingDirectory, "empty-tail.ogg");
		using (var stream = File.Create(path))
		{
			OggStreamBuilder.WritePage(stream, new[] { OggStreamBuilder.OpusHead(1) });
			OggStreamBuilder.WritePage(stream, new[] { OggStreamBuilder.OpusTags() }, pageSequence: 1);
			OggStreamBuilder.WritePage(stream, new[] { new[] { HybridSwb20MsMonoCode3, SixCbrFrames } }, pageSequence: 2);
			OggStreamBuilder.WritePage(stream, new[] { Array.Empty<byte>() }, pageSequence: 3);
		}

		Assert.Equal(6 * 960, CountSamples(path));
	}

	[Fact]
	public void StereoHeader_IsReportedAndDownmixed()
	{
		// Config 13 with the stereo bit set, code 3, six CBR frames.
		const byte stereoToc = HybridSwb20MsMonoCode3 | 0x04;
		string path = WriteFixture("stereo.ogg", channels: 2, preSkip: 0, new[] { stereoToc, SixCbrFrames });

		Assert.Equal(2, OggOpusPcmDecoder.ReadChannelCount(path));
		// Mono output: one sample per frame position, not two.
		Assert.Equal(6 * 960, CountSamples(path));
	}

	[Fact]
	public void IsOggOpus_DetectsOpusAndRejectsEverythingElse()
	{
		string opus = WriteFixture("detect.ogg", channels: 1, preSkip: 0,
			new[] { HybridSwb20MsMonoCode3, SixCbrFrames });

		string wav = Path.Combine(_workingDirectory, "not-opus.wav");
		File.WriteAllBytes(wav, "RIFF\0\0\0\0WAVEfmt "u8.ToArray());

		string vorbis = Path.Combine(_workingDirectory, "vorbis.ogg");
		using (var stream = File.Create(vorbis))
		{
			// An Ogg page carrying Vorbis instead of Opus — Concentus cannot decode it, so it
			// must fall through to the caller's Media Foundation path.
			var identification = new byte[30];
			identification[0] = 1;
			"vorbis"u8.ToArray().CopyTo(identification, 1);
			OggStreamBuilder.WritePage(stream, new[] { identification });
		}

		Assert.True(OggOpusPcmDecoder.IsOggOpus(opus));
		Assert.False(OggOpusPcmDecoder.IsOggOpus(wav));
		Assert.False(OggOpusPcmDecoder.IsOggOpus(vorbis));
		Assert.False(OggOpusPcmDecoder.IsOggOpus(Path.Combine(_workingDirectory, "missing.ogg")));
	}

	[Fact]
	public void SurroundOpus_IsNotClaimedAndFailsWithAReadableMessage()
	{
		// Concentus decodes mono and stereo only. A 6-channel file must not be claimed by this
		// decoder, so the caller falls through to its own fallback rather than the codec
		// throwing a bare "Number of channels must be 1 or 2" at the user.
		string path = WriteFixture("surround.ogg", channels: 6, preSkip: 0,
			new[] { HybridSwb20MsMonoCode3, SixCbrFrames });

		Assert.False(OggOpusPcmDecoder.IsOggOpus(path));

		var error = Assert.Throws<NotSupportedException>(() => OggOpusPcmDecoder.DecodeToMonoPcm(path, _ => { }));
		Assert.Contains("6-channel", error.Message);
	}

	[Fact]
	public void NonOpusFile_ThrowsRatherThanReturningSilence()
	{
		string wav = Path.Combine(_workingDirectory, "throw.wav");
		File.WriteAllBytes(wav, "RIFF\0\0\0\0WAVEfmt "u8.ToArray());

		Assert.Throws<InvalidDataException>(() => OggOpusPcmDecoder.DecodeToMonoPcm(wav, _ => { }));
	}

	// A multiplexed container carries several logical streams side by side — Opus audio next to
	// Theora video, say. Without serial-number filtering every stream's packets were fed to the
	// one Opus decoder; those that happened to parse as a plausible TOC byte decoded as noise.
	[Fact]
	public void PacketsFromOtherLogicalStreams_AreIgnored()
	{
		const uint opusSerial = 7;
		const uint videoSerial = 8;

		string path = Path.Combine(_workingDirectory, "multiplexed.ogg");
		using (var stream = File.Create(path))
		{
			OggStreamBuilder.WritePage(stream, new[] { OggStreamBuilder.OpusHead(1) }, serial: opusSerial);
			// The other stream's own identification header, as a real multiplexed file would have.
			OggStreamBuilder.WritePage(stream, new[] { Filled(42, 0x80) }, serial: videoSerial);
			OggStreamBuilder.WritePage(stream, new[] { OggStreamBuilder.OpusTags() }, serial: opusSerial, pageSequence: 1);
			OggStreamBuilder.WritePage(stream, new[] { new[] { HybridSwb20MsMonoCode3, SixCbrFrames } }, serial: opusSerial, pageSequence: 2);
			// Bytes that parse as a perfectly good six-frame Opus packet, but belong to the
			// other stream. Unfiltered, these decode as another 120 ms of noise.
			OggStreamBuilder.WritePage(stream, new[] { new[] { HybridSwb20MsMonoCode3, SixCbrFrames } }, serial: videoSerial, pageSequence: 1);
		}

		// Only the six frames of the one Opus packet: the foreign stream contributes nothing.
		Assert.Equal(6 * 960, CountSamples(path));
	}

	// A chained file is several separately encoded sections concatenated, each with its own
	// OpusHead and its own serial. Only the first chain is decoded, so its pre-skip is not
	// re-applied to audio it does not belong to.
	[Fact]
	public void ChainedStreams_AfterTheFirstAreIgnored()
	{
		string path = Path.Combine(_workingDirectory, "chained.ogg");
		using (var stream = File.Create(path))
		{
			OggStreamBuilder.WritePage(stream, new[] { OggStreamBuilder.OpusHead(1) }, serial: 1);
			OggStreamBuilder.WritePage(stream, new[] { OggStreamBuilder.OpusTags() }, serial: 1, pageSequence: 1);
			OggStreamBuilder.WritePage(stream, new[] { new[] { HybridSwb20MsMonoCode3, SixCbrFrames } }, serial: 1, pageSequence: 2);

			OggStreamBuilder.WritePage(stream, new[] { OggStreamBuilder.OpusHead(1) }, serial: 2);
			OggStreamBuilder.WritePage(stream, new[] { OggStreamBuilder.OpusTags() }, serial: 2, pageSequence: 1);
			OggStreamBuilder.WritePage(stream, new[] { new[] { HybridSwb20MsMonoCode3, SixCbrFrames } }, serial: 2, pageSequence: 2);
		}

		Assert.Equal(6 * 960, CountSamples(path));
	}

	[Fact]
	public void DecodeToPcmStream_ReturnsTwoLittleEndianBytesPerSample()
	{
		string path = WriteFixture("bytes.ogg", channels: 1, preSkip: 0,
			new[] { HybridSwb20MsMonoCode3, SixCbrFrames });

		int sampleCount = CountSamples(path);

		using MemoryStream pcm = OggOpusPcmDecoder.DecodeToPcmStream(path);

		Assert.Equal(0, pcm.Position);
		Assert.Equal(sampleCount * 2L, pcm.Length);

		// Same bytes the caller-side short-to-byte loop used to produce.
		var expected = new List<byte>();
		OggOpusPcmDecoder.DecodeToMonoPcm(path, samples =>
		{
			foreach (short sample in samples)
			{
				expected.Add((byte)(sample & 0xFF));
				expected.Add((byte)((sample >> 8) & 0xFF));
			}
		});

		Assert.Equal(expected, pcm.ToArray());
	}

	[Fact]
	public void DecodeToPcmStream_DoesNotLeaveAStreamBehindWhenTheFileIsUnreadable()
	{
		string wav = Path.Combine(_workingDirectory, "bad.wav");
		File.WriteAllBytes(wav, "RIFF\0\0\0\0WAVEfmt "u8.ToArray());

		Assert.Throws<InvalidDataException>(() => OggOpusPcmDecoder.DecodeToPcmStream(wav));
	}

	private static byte[] Filled(int length, byte value)
	{
		var data = new byte[length];
		Array.Fill(data, value);
		return data;
	}
}
