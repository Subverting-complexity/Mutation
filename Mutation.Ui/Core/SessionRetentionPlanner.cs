using CognitiveSupport;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mutation.Ui.Core;

/// <summary>
/// Pure decision logic for session retention cleanup: given the current sessions,
/// the set of paths that must never be deleted, and the retention cap, decide which
/// session files to delete. Kept free of any filesystem access so it is unit-testable
/// in isolation; <see cref="SpeechToTextManager"/> owns the actual deletion and its
/// best-effort error handling.
/// </summary>
internal static class SessionRetentionPlanner
{
	/// <summary>
	/// Returns the session file paths to delete, oldest first, to bring the retained
	/// count down to <paramref name="maxRetained"/>. Excluded sessions (e.g. the active
	/// recording) are never deleted and still count toward the retained total, so the
	/// active recording is preserved without shrinking the kept window below the cap.
	/// </summary>
	/// <param name="exclusions">
	/// Paths that must survive. Matched with <see cref="PathEquality.SamePath"/> rather than
	/// as strings: an exclusion spelled differently from the path the session scan produced
	/// would otherwise miss, and the recording the user has selected would be deleted
	/// underneath them (issue #306).
	/// </param>
	public static IReadOnlyList<SpeechSession> SelectSessionsToDelete(
		IReadOnlyCollection<SpeechSession> sessions,
		IReadOnlyCollection<string> exclusions,
		int maxRetained)
	{
		ArgumentNullException.ThrowIfNull(sessions);
		ArgumentNullException.ThrowIfNull(exclusions);

		int currentCount = sessions.Count;
		if (currentCount <= maxRetained)
			return Array.Empty<SpeechSession>();

		var toDelete = new List<SpeechSession>();
		foreach (var session in sessions.OrderBy(s => s.Timestamp))
		{
			if (currentCount <= maxRetained)
				break;

			if (exclusions.Any(excluded => PathEquality.SamePath(excluded, session.FilePath)))
				continue;

			toDelete.Add(session);
			currentCount--;
		}

		return toDelete;
	}
}
