namespace CognitiveSupport.Extensions;

public static class ObjectExtensions
{
	/// <summary>
	/// Signals retry progress audibly: one beep per attempt, so a third retry is
	/// distinguishable from a first. <paramref name="attempt"/> is 1-based, and the
	/// first (non-retry) attempt is silent — there is nothing to report yet.
	/// </summary>
	public static void Beep(
			  this object caller,
			  int attempt)
	{
#pragma warning disable CA1416 // Validate platform compatibility
		if (!RetryBeepPolicy.ShouldBeep(attempt))
			return;

		// One call, one sound. Looping BeepPlayer.Play here used to collapse to a
		// single audible beep because of its duplicate-suppression window (issue #216).
		BeepPlayer.PlayRepeated(BeepType.End, RetryBeepPolicy.RepeatCountFor(attempt));
#pragma warning restore CA1416 // Validate platform compatibility
	}

}
