using System;
using System.Collections.Generic;
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
	/// Picks the selection for a freshly rebuilt session list: the caller's preferred
	/// recording when it names one and it is still there, otherwise whatever was selected
	/// before if that is still there, otherwise the newest recording — and nothing at all
	/// once the list is empty.
	/// </summary>
	/// <param name="currentSelection">
	/// What was selected before the rebuild. Kept when no preferred path is given and the
	/// same recording is still listed; dropped in favour of the newest one when it is not,
	/// because retention cleanup can delete the selected file underneath the user and
	/// leaving it selected turns Play into "Audio file not found" (issue #303).
	/// </param>
	/// <returns>
	/// An entry of <paramref name="sessions"/>, never a stale instance from a previous
	/// rebuild — callers look the result up by index, which only works for an item the list
	/// actually holds.
	/// </returns>
	public static SpeechSession? ChooseSelection(
		IReadOnlyList<SpeechSession> sessions,
		SpeechSession? currentSelection,
		string? preferredPath)
	{
		if (sessions is null) throw new ArgumentNullException(nameof(sessions));

		string? wantedPath = string.IsNullOrWhiteSpace(preferredPath)
			? currentSelection?.FilePath
			: preferredPath;

		SpeechSession? selection = sessions.FirstOrDefault(s => PathEquality.SamePath(s.FilePath, wantedPath));

		if (selection is null && sessions.Count > 0)
			selection = sessions[0];

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
