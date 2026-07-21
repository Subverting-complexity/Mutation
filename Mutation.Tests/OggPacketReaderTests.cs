using System.Collections.Generic;
using System.IO;
using System.Linq;
using CognitiveSupport;

namespace Mutation.Tests;

public class OggPacketReaderTests
{
	private static List<byte[]> Read(Stream stream) => OggPacketReader.ReadPackets(stream).ToList();

	private static byte[] Filled(int length, byte value)
	{
		var data = new byte[length];
		for (int i = 0; i < length; i++) data[i] = value;
		return data;
	}

	[Fact]
	public void ReadsMultiplePacketsFromOnePage()
	{
		using var stream = new MemoryStream();
		OggStreamBuilder.WritePage(stream, new[] { Filled(10, 1), Filled(20, 2) });
		stream.Position = 0;

		var packets = Read(stream);

		Assert.Equal(2, packets.Count);
		Assert.Equal(10, packets[0].Length);
		Assert.Equal(20, packets[1].Length);
		Assert.All(packets[1], b => Assert.Equal(2, b));
	}

	[Fact]
	public void ReassemblesPacketAcrossSegments()
	{
		var packet = Filled(600, 7); // 255 + 255 + 90
		using var stream = new MemoryStream();
		OggStreamBuilder.WritePage(stream, OggStreamBuilder.ToSegments(packet));
		stream.Position = 0;

		var packets = Read(stream);

		Assert.Single(packets);
		Assert.Equal(600, packets[0].Length);
		Assert.All(packets[0], b => Assert.Equal(7, b));
	}

	[Fact]
	public void PacketWhoseLengthIsAMultipleOf255_IsTerminatedByZeroSegment()
	{
		var packet = Filled(255, 3);
		using var stream = new MemoryStream();
		// ToSegments appends the required empty terminating segment.
		OggStreamBuilder.WritePage(stream, OggStreamBuilder.ToSegments(packet));
		stream.Position = 0;

		var packets = Read(stream);

		Assert.Single(packets);
		Assert.Equal(255, packets[0].Length);
	}

	[Fact]
	public void ReassemblesPacketSpanningTwoPages()
	{
		using var stream = new MemoryStream();
		OggStreamBuilder.WritePage(stream, new[] { Filled(255, 4) }); // no terminator: continues
		OggStreamBuilder.WritePage(stream, new[] { Filled(30, 5) }, continued: true, pageSequence: 1);
		stream.Position = 0;

		var packets = Read(stream);

		Assert.Single(packets);
		Assert.Equal(285, packets[0].Length);
		Assert.Equal(4, packets[0][254]);
		Assert.Equal(5, packets[0][255]);
	}

	[Fact]
	public void StopsCleanlyOnATruncatedTail()
	{
		using var buffer = new MemoryStream();
		OggStreamBuilder.WritePage(buffer, new[] { Filled(10, 1) });
		byte[] complete = buffer.ToArray();

		// Append a page header that promises more data than the file actually holds.
		using var truncated = new MemoryStream();
		truncated.Write(complete);
		OggStreamBuilder.WritePage(truncated, new[] { Filled(40, 9) }, pageSequence: 1);
		truncated.SetLength(truncated.Length - 20);
		truncated.Position = 0;

		var packets = Read(truncated);

		Assert.Single(packets);
		Assert.Equal(10, packets[0].Length);
	}

	[Fact]
	public void StopsWhenTheCapturePatternIsMissing()
	{
		using var stream = new MemoryStream();
		stream.Write("NotAnOggFileAtAllReallyNoPages"u8);
		stream.Position = 0;

		Assert.Empty(Read(stream));
	}
}
