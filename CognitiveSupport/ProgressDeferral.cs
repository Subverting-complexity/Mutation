namespace CognitiveSupport;

// State machine for deferring periodic progress announcements to a sentence boundary.
// When the read crosses a percentage threshold mid-sentence the announcement is held
// "pending" rather than interjected immediately, so it can be spoken cleanly between
// sentences instead of cutting the current one off mid-way. Kept synthesizer-free and
// self-contained so the timing rules can be unit-tested in isolation.
public struct ProgressDeferral
{
	// Highest threshold already crossed and accounted for (0 = none). Crossed thresholds
	// are recorded here even before they are spoken, so a burst of thresholds before the
	// next boundary collapses to a single announcement of the highest.
	public int LastAnnouncedPercent;

	// Highest threshold waiting to be spoken at the next sentence boundary (0 = nothing
	// pending).
	public int PendingPercent;

	// Record a progress tick. If the read has crossed a new threshold, mark every
	// threshold up to the highest as passed and hold the highest as pending. Returns true
	// when a new threshold became pending. Multiple ticks before a boundary keep only the
	// highest pending, because LastAnnouncedPercent advances with each crossing.
	public bool Observe(int currentPercent, int stepPercent)
	{
		int crossed = ReadingProgress.HighestThresholdCrossed(LastAnnouncedPercent, currentPercent, stepPercent);
		if (crossed <= 0) return false;

		LastAnnouncedPercent = crossed;
		PendingPercent = crossed;
		return true;
	}

	// Take the pending announcement at a sentence boundary, clearing it. Returns 0 when
	// nothing is pending.
	public int TakeAtBoundary()
	{
		int pending = PendingPercent;
		PendingPercent = 0;
		return pending;
	}

	// Take the pending announcement when the read completes on its final sentence. The
	// read has reached the end (100%), which is never announced as progress, so a pending
	// threshold is dropped. The `< 100%` guard is defensive: a non-terminal completion
	// would still flush. Clears the pending state either way.
	public int TakeOnCompletion(int finalPercent)
	{
		int pending = PendingPercent;
		PendingPercent = 0;
		return finalPercent < 100 ? pending : 0;
	}

	// Forget all threshold tracking — a fresh read or restart-from-beginning starts over.
	public void Reset()
	{
		LastAnnouncedPercent = 0;
		PendingPercent = 0;
	}
}
