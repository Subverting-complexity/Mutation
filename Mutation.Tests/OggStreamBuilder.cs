using System;
using System.Collections.Generic;
using System.IO;

namespace Mutation.Tests;

/// <summary>
/// Builds Ogg pages by hand so container-level edge cases (multi-segment packets, packets
/// spanning pages, DTX-bearing Opus packets) can be tested without depending on an encoder
/// that can produce them.
/// </summary>
internal static class OggStreamBuilder
{
	/// <summary>
	/// Writes one Ogg page containing the given segments verbatim. CRC is left zero — the
	/// reader under test does not verify it, which is what lets tests hand-assemble pages.
	/// </summary>
	public static void WritePage(Stream stream, IEnumerable<byte[]> segments, bool continued = false, long granule = 0, int pageSequence = 0)
	{
		var segmentList = new List<byte[]>(segments);

		var header = new byte[27];
		header[0] = (byte)'O'; header[1] = (byte)'g'; header[2] = (byte)'g'; header[3] = (byte)'S';
		header[4] = 0; // stream structure version
		header[5] = (byte)(continued ? 0x01 : 0x00);
		BitConverter.GetBytes(granule).CopyTo(header, 6);
		BitConverter.GetBytes(1).CopyTo(header, 14); // serial
		BitConverter.GetBytes(pageSequence).CopyTo(header, 18);
		header[26] = (byte)segmentList.Count;

		stream.Write(header, 0, header.Length);
		foreach (var segment in segmentList)
			stream.WriteByte((byte)segment.Length);
		foreach (var segment in segmentList)
			stream.Write(segment, 0, segment.Length);
	}

	/// <summary>
	/// Splits a packet into the 255-byte lacing segments Ogg requires.
	/// </summary>
	public static List<byte[]> ToSegments(byte[] packet)
	{
		var segments = new List<byte[]>();
		int offset = 0;
		while (packet.Length - offset >= 255)
		{
			segments.Add(packet[offset..(offset + 255)]);
			offset += 255;
		}

		segments.Add(packet[offset..]);
		return segments;
	}

	/// <summary>
	/// The 19-byte OpusHead identification header (RFC 7845 §5.1).
	/// </summary>
	public static byte[] OpusHead(int channels, int preSkip = 0)
	{
		var head = new byte[19];
		"OpusHead"u8.ToArray().CopyTo(head, 0);
		head[8] = 1; // version
		head[9] = (byte)channels;
		head[10] = (byte)(preSkip & 0xFF);
		head[11] = (byte)((preSkip >> 8) & 0xFF);
		BitConverter.GetBytes(48000).CopyTo(head, 12);
		return head;
	}

	public static byte[] OpusTags()
	{
		using var buffer = new MemoryStream();
		buffer.Write("OpusTags"u8);
		buffer.Write(BitConverter.GetBytes(0)); // vendor string length
		buffer.Write(BitConverter.GetBytes(0)); // comment count
		return buffer.ToArray();
	}
}
