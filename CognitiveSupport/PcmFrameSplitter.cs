using System;

namespace CognitiveSupport;

/// <summary>
/// Splits a stream of 16-bit little-endian PCM bytes into fixed-size sample frames
/// without per-byte list growth or per-frame list shifts.
///
/// Feed raw capture/decoder bytes via <see cref="Append"/>; each completed frame is
/// handed to the supplied callback as a freshly allocated <c>short[]</c>. Ownership of
/// that array passes to the callback, so downstream code such as
/// <see cref="SilenceTrimmer"/> — which retains frame references across calls — is safe.
/// Call <see cref="Flush"/> once the stream ends to emit or drop the trailing partial
/// frame.
///
/// Bytes that do not complete a frame are held in a single fixed carry buffer and
/// combined with the next <see cref="Append"/>, so the class allocates only the frames
/// it emits — no intermediate copies and no O(n) buffer shifts.
///
/// Not thread-safe; the caller must serialize access.
/// </summary>
public sealed class PcmFrameSplitter
{
	private const int BytesPerSample = 2; // 16-bit PCM

	private readonly int _frameSamples;
	private readonly int _frameBytes;

	// Holds the bytes of an as-yet-incomplete frame between Append calls. Never handed
	// to the callback directly; ToFrame copies out of it into a fresh array.
	private readonly byte[] _carry;
	private int _carryLength;

	/// <param name="frameSamples">Samples per emitted frame (e.g. 960 for 20 ms at 48 kHz).</param>
	public PcmFrameSplitter(int frameSamples)
	{
		if (frameSamples <= 0)
			throw new ArgumentOutOfRangeException(nameof(frameSamples));

		_frameSamples = frameSamples;
		_frameBytes = frameSamples * BytesPerSample;
		_carry = new byte[_frameBytes];
	}

	/// <summary>
	/// Appends <paramref name="count"/> bytes from <paramref name="source"/>, emitting a
	/// fresh <c>short[]</c> for every complete frame those bytes form (together with any
	/// carried-over remainder). Leftover bytes are retained for the next call.
	/// </summary>
	public void Append(byte[] source, int count, Action<short[]> emit)
	{
		if (source is null) throw new ArgumentNullException(nameof(source));
		if (emit is null) throw new ArgumentNullException(nameof(emit));
		if (count < 0 || count > source.Length)
			throw new ArgumentOutOfRangeException(nameof(count));

		int offset = 0;

		// Top up an in-progress carry frame first, then emit it once full.
		if (_carryLength > 0)
		{
			int need = _frameBytes - _carryLength;
			int take = Math.Min(need, count);
			Buffer.BlockCopy(source, 0, _carry, _carryLength, take);
			_carryLength += take;
			offset += take;

			if (_carryLength < _frameBytes)
				return; // consumed everything, still short of a full frame

			emit(ToFrame(_carry, 0));
			_carryLength = 0;
		}

		// Emit whole frames straight from the source with no intermediate copy.
		int remaining = count - offset;
		while (remaining >= _frameBytes)
		{
			emit(ToFrame(source, offset));
			offset += _frameBytes;
			remaining -= _frameBytes;
		}

		// Stash the leftover tail (< one frame) for the next Append.
		if (remaining > 0)
		{
			Buffer.BlockCopy(source, offset, _carry, 0, remaining);
			_carryLength = remaining;
		}
	}

	/// <summary>
	/// Appends already-decoded 16-bit samples, emitting a fresh <c>short[]</c> for every
	/// complete frame. Used by decoders that hand back <c>short[]</c> directly (Opus) rather
	/// than a raw byte stream; framing and carry-over behave exactly as in <see cref="Append"/>.
	/// Kept as a distinct name rather than an overload so a <c>null</c> literal argument stays
	/// unambiguous at the call site.
	/// </summary>
	public void AppendSamples(short[] source, int count, Action<short[]> emit)
	{
		if (source is null) throw new ArgumentNullException(nameof(source));
		if (count < 0 || count > source.Length)
			throw new ArgumentOutOfRangeException(nameof(count));

		var bytes = new byte[count * BytesPerSample];
		Buffer.BlockCopy(source, 0, bytes, 0, bytes.Length);
		Append(bytes, bytes.Length, emit);
	}

	/// <summary>
	/// Emits any buffered partial frame. When <paramref name="padWithSilence"/> is true a
	/// non-empty remainder is zero-padded to a full frame and emitted; otherwise it is
	/// dropped. The carry buffer is cleared either way.
	/// </summary>
	public void Flush(Action<short[]> emit, bool padWithSilence)
	{
		if (emit is null) throw new ArgumentNullException(nameof(emit));
		if (_carryLength == 0) return;

		if (padWithSilence)
		{
			Array.Clear(_carry, _carryLength, _frameBytes - _carryLength);
			emit(ToFrame(_carry, 0));
		}

		_carryLength = 0;
	}

	private short[] ToFrame(byte[] source, int byteOffset)
	{
		var frame = new short[_frameSamples];
		Buffer.BlockCopy(source, byteOffset, frame, 0, _frameBytes);
		return frame;
	}
}
