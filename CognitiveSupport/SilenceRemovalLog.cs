using System;
using System.Collections.Generic;

namespace CognitiveSupport;

/// <summary>
/// Carries the silence-removal points from whichever preprocessing pass produced an
/// audio file through to whoever later transcribes it.
///
/// It remembers one file, because that is all the app ever needs: preprocessing and
/// transcription are two steps of the same user action (stop recording and transcribe;
/// import a file and transcribe it), so the file just processed is the file about to be
/// sent. Anything older — a session picked out of history after a restart — has no
/// points, and chunking falls back to cutting at the furthest safe position.
///
/// Thread-safe: preprocessing finishes on a worker thread and the transcription may be
/// started from another.
/// </summary>
public sealed class SilenceRemovalLog
{
	private readonly object _gate = new();
	private string? _path;
	private IReadOnlyList<SilenceRemovalPoint> _points = Array.Empty<SilenceRemovalPoint>();

	public void Record(string audioFilePath, IReadOnlyList<SilenceRemovalPoint>? points)
	{
		if (string.IsNullOrWhiteSpace(audioFilePath))
			return;

		lock (_gate)
		{
			_path = audioFilePath;
			_points = points ?? Array.Empty<SilenceRemovalPoint>();
		}
	}

	/// <summary>
	/// The points recorded for this file, or an empty list if the file is not the one
	/// most recently preprocessed.
	/// <para>
	/// Matched with <see cref="PathEquality.SamePath"/> rather than a raw string compare, so
	/// the answer here agrees with the rest of the app when the caller spells the recording
	/// differently from the way preprocessing recorded it (issue #324).
	/// </para>
	/// </summary>
	public IReadOnlyList<SilenceRemovalPoint> PointsFor(string audioFilePath)
	{
		if (string.IsNullOrWhiteSpace(audioFilePath))
			return Array.Empty<SilenceRemovalPoint>();

		lock (_gate)
		{
			return PathEquality.SamePath(_path, audioFilePath)
				? _points
				: Array.Empty<SilenceRemovalPoint>();
		}
	}
}
