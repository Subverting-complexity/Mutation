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
	public void NonOpusFile_ThrowsRatherThanReturningSilence()
	{
		string wav = Path.Combine(_workingDirectory, "throw.wav");
		File.WriteAllBytes(wav, "RIFF\0\0\0\0WAVEfmt "u8.ToArray());

		Assert.Throws<InvalidDataException>(() => OggOpusPcmDecoder.DecodeToMonoPcm(wav, _ => { }));
	}
}
