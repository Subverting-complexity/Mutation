using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace CognitiveSupport;

/// <summary>
/// Minimal Ogg container demuxer: yields each logical bitstream's packets in order, tagged
/// with the serial number of the stream they belong to.
///
/// Concentus.Oggfile 1.0.6's own reader cannot be used for arbitrary third-party files —
/// it stops producing audio partway through and then spins forever, because
/// <c>HasNextPacket</c> stays true after the stream is exhausted while
/// <c>DecodeNextPacket</c> returns null. Demuxing here (RFC 3533 §6) keeps the container
/// concern separate from the codec and lets <see cref="OggOpusPcmDecoder"/> handle packets
/// that the bundled reader chokes on.
/// </summary>
public static class OggPacketReader
{
	private const int PageHeaderBytes = 27;
	private const int SerialNumberOffset = 14;
	private const int SegmentTableOffset = 26;
	private const int ContinuedPacketFlag = 0x01;
	private const int MaxSegmentLength = 255;

	/// <summary>
	/// Enumerates the packets of the stream, reassembling segments that a packet is split
	/// across (including across page boundaries). Enumeration stops at the first page that
	/// does not start with the "OggS" capture pattern, which is how a truncated or corrupt
	/// tail is tolerated rather than throwing away everything decoded so far.
	/// </summary>
	public static IEnumerable<OggPacket> ReadPackets(Stream stream)
	{
		if (stream is null) throw new ArgumentNullException(nameof(stream));

		var header = new byte[PageHeaderBytes];

		// One partial-packet buffer per logical stream. A multiplexed container interleaves
		// pages from several streams, so a single shared buffer would splice one stream's
		// continued packet onto segments belonging to another.
		var partials = new Dictionary<uint, List<byte>>();

		while (true)
		{
			if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
				yield break;

			if (header[0] != (byte)'O' || header[1] != (byte)'g' || header[2] != (byte)'g' || header[3] != (byte)'S')
				yield break;

			uint serialNumber = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(SerialNumberOffset));
			if (!partials.TryGetValue(serialNumber, out var packet))
			{
				packet = new List<byte>();
				partials[serialNumber] = packet;
			}

			// A page whose continuation flag is clear starts a fresh packet; anything held over
			// from a truncated previous page of the same stream is then stale and must be dropped.
			if ((header[5] & ContinuedPacketFlag) == 0)
				packet.Clear();

			int segmentCount = header[SegmentTableOffset];
			var segmentTable = new byte[segmentCount];
			if (stream.ReadAtLeast(segmentTable, segmentCount, throwOnEndOfStream: false) < segmentCount)
				yield break;

			bool truncated = false;
			for (int i = 0; i < segmentCount; i++)
			{
				int length = segmentTable[i];
				if (length > 0)
				{
					var segment = new byte[length];
					if (stream.ReadAtLeast(segment, length, throwOnEndOfStream: false) < length)
					{
						truncated = true;
						break;
					}

					packet.AddRange(segment);
				}

				// A segment shorter than 255 bytes terminates the packet; a full-length one
				// means it continues in the next segment (possibly on the next page).
				if (length < MaxSegmentLength)
				{
					yield return new OggPacket(packet.ToArray(), serialNumber);
					packet.Clear();
				}
			}

			if (truncated)
				yield break;
		}
	}
}
