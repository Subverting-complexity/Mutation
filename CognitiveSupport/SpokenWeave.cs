using System.Text;

namespace CognitiveSupport;

// Pure spoken-text weaving for gapless progress announcements. Builds the single
// string handed to the synthesizer — an optional spoken-only prefix, the real
// text from `fromReal` onward, and zero or more progress markers spliced in at
// sentence boundaries — together with a map that translates a character position
// in that woven string back to an offset in the *real* text.
//
// The map exists because the real-text offsets (`_currentPosition`, sentence
// starts, the percentage, skip targets) must stay anchored to the original text;
// a woven marker shifts every spoken position after it, and that shift is what
// the map undoes. Side-effect free and static so the mapping is unit-testable.
public static class SpokenWeave
{
	// A progress marker to splice in at `RealOffset` (a sentence boundary), with the
	// already-phrased `Text` (including any trailing space) and the `Percent` it
	// announces.
	public readonly record struct Marker(int RealOffset, int Percent, string Text);

	// Build the woven spoken string for a read starting at `fromReal`. `prefix` is
	// spoken-only text placed before the real content (a length announcement or
	// "Beginning of text. "); pass "" for none. `markers` must be ascending by
	// `RealOffset`, each at or after `fromReal`.
	public static (string Spoken, SpokenWeaveMap Map) Build(
		string realText, int fromReal, string? prefix, IReadOnlyList<Marker> markers)
	{
		prefix ??= string.Empty;
		int length = realText.Length;
		fromReal = Math.Clamp(fromReal, 0, length);

		var sb = new StringBuilder(prefix);
		var mapped = new List<SpokenWeaveMap.Entry>();
		int prevReal = fromReal;

		if (markers is not null)
		{
			foreach (Marker m in markers)
			{
				int offset = Math.Clamp(m.RealOffset, prevReal, length);
				sb.Append(realText, prevReal, offset - prevReal);
				int spokenStart = sb.Length;
				sb.Append(m.Text);
				mapped.Add(new SpokenWeaveMap.Entry(spokenStart, m.Text.Length, offset, m.Percent));
				prevReal = offset;
			}
		}

		sb.Append(realText, prevReal, length - prevReal);

		// real = spoken + baseDelta for spoken positions before the first marker
		// (prefix length pushes the real content right; fromReal shifts it back).
		int baseDelta = fromReal - prefix.Length;
		return (sb.ToString(), new SpokenWeaveMap(baseDelta, length, mapped));
	}
}

// Translates a position in a woven spoken string back to a real-text offset, and
// reports which woven progress markers a real position has reached. Immutable.
public readonly struct SpokenWeaveMap
{
	// One spliced marker: its span in the spoken string, the real offset it sits
	// at, and the percentage it announces.
	public readonly record struct Entry(int SpokenStart, int Length, int RealOffset, int Percent);

	private readonly int _baseDelta;
	private readonly int _realLength;
	private readonly IReadOnlyList<Entry>? _markers;

	public SpokenWeaveMap(int baseDelta, int realLength, IReadOnlyList<Entry> markers)
	{
		_baseDelta = baseDelta;
		_realLength = realLength;
		_markers = markers ?? Array.Empty<Entry>();
	}

	// A map with nothing woven in, for a field or local that has no read in flight yet.
	// `default(SpokenWeaveMap)` behaves identically — every member below tolerates the
	// null marker list a defaulted struct leaves behind — but naming it says so out loud.
	public static SpokenWeaveMap Empty => default;

	// Marker count, treating a defaulted struct (which bypasses the constructor and so
	// never gets the empty list) as having none. Both members below go through this
	// rather than touching _markers.Count, which would throw on a defaulted map — on the
	// synthesizer's callback thread, where an unhandled exception ends the process.
	private int MarkerCount => _markers?.Count ?? 0;

	// Map a spoken-string position back to a real-text offset. Positions before the
	// first marker shift by the base delta; each marker crossed adds its length to
	// the shift; a position inside a marker maps to that marker's boundary.
	public int ToReal(int spokenPosition)
	{
		int real = spokenPosition + _baseDelta;
		for (int i = 0; i < MarkerCount; i++)
		{
			Entry m = _markers![i];
			if (spokenPosition < m.SpokenStart) break;
			if (spokenPosition < m.SpokenStart + m.Length) return m.RealOffset;
			real -= m.Length;
		}
		return Math.Clamp(real, 0, _realLength);
	}

	// The highest threshold among the woven markers whose boundary the read has
	// reached (real offset at or before `realPosition`), or 0 when none have been
	// reached. Used to advance the "last announced" tracker as each baked
	// announcement plays, so a later re-plan never re-announces it.
	public int HighestMarkerPercentAtOrBefore(int realPosition)
	{
		int best = 0;
		for (int i = 0; i < MarkerCount; i++)
		{
			Entry m = _markers![i];
			if (m.RealOffset <= realPosition && m.Percent > best)
				best = m.Percent;
		}
		return best;
	}
}
