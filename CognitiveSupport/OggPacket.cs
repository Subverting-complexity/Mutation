namespace CognitiveSupport;

/// <summary>
/// One demuxed Ogg packet, together with the logical bitstream it came from.
///
/// An Ogg container can interleave several logical streams — video alongside audio, or one
/// chain of encoded sections after another — and the page header's serial number is the only
/// thing that tells them apart (RFC 3533 §6). Carrying it alongside the payload lets a
/// consumer keep the one stream it can decode and ignore everybody else's packets.
/// </summary>
/// <param name="Data">The reassembled packet payload.</param>
/// <param name="SerialNumber">The logical bitstream serial number of the page(s) it came from.</param>
public readonly record struct OggPacket(byte[] Data, uint SerialNumber);
