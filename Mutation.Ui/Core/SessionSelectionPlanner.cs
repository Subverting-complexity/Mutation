using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mutation.Ui.Core;

/// <summary>
/// Decides which recording is selected after the session list is rebuilt, and which one
/// Previous/Next moves to.
/// <para>
/// Both are pure arithmetic over a list, but neither could be exercised inside
/// <see cref="AudioSessionManager"/>, which needs a recorder, a device enumerator, and a
/// transcription service to exist at all. Getting either wrong is silent: the wrong
/// recording is announced and played, and nothing reports an error.
/// </para>
/// </summary>
internal static class SessionSelectionPlanner
{
	/// <summary>
	/// Whether two recording paths name the same file. Different spellings of one path —
	/// relative versus absolute, mixed case — have to compare equal, because the path a
	/// caller remembered and the path the session list rebuilt from need not match
	/// character for character. A blank path matches nothing, not even another blank.
	/// </summary>
	public static bool PathsEqual(string? first, string? second)
	{
		if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
			return false;

		return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Picks the selection for a freshly rebuilt session list: the caller's preferred
	/// recording when it names one and it is still there, otherwise the newest recording,
	/// and nothing at all once the list is empty.
	/// </summary>
	/// <param name="currentSelection">
	/// What was selected before the rebuild. Kept when no preferred path is given and the
	/// list is not empty — including when it is no longer in the list, which is what the
	/// caller does today and is pinned by test rather than endorsed.
	/// </param>
	public static SpeechSession? ChooseSelection(
		IReadOnlyList<SpeechSession> sessions,
		SpeechSession? currentSelection,
		string? preferredPath)
	{
		if (sessions is null) throw new ArgumentNullException(nameof(sessions));

		SpeechSession? selection = currentSelection;

		if (!string.IsNullOrWhiteSpace(preferredPath))
			selection = sessions.FirstOrDefault(s => PathsEqual(s.FilePath, preferredPath));

		if (selection is null && sessions.Count > 0)
			selection = sessions[0];
		else if (sessions.Count == 0)
			selection = null;

		return selection;
	}

	/// <summary>
	/// The index Previous/Next moves to, or null when the move would run off either end —
	/// the list does not wrap, so the newest and oldest recordings are hard stops.
	/// </summary>
	/// <param name="currentIndex">
	/// Where the selection sits, or a negative value when nothing is selected — which starts
	/// the move from the newest recording rather than refusing it.
	/// </param>
	/// <param name="direction">Negative moves towards the newest end; anything else moves away from it.</param>
	public static int? NextIndex(int count, int currentIndex, int direction)
	{
		if (count <= 0)
			return null;

		if (currentIndex < 0)
			currentIndex = 0;

		int targetIndex = direction < 0 ? currentIndex - 1 : currentIndex + 1;
		if (targetIndex < 0 || targetIndex >= count)
			return null;

		return targetIndex;
	}
}
