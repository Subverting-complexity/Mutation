namespace CognitiveSupport;

/// <summary>
/// Decides how a retry attempt is signalled audibly. Kept separate from the playback
/// call so the policy is verifiable without an audio device (issue #216).
/// </summary>
public static class RetryBeepPolicy
{
	/// <summary>
	/// The first attempt is not a retry, so it stays silent — beeping there made a
	/// first attempt indistinguishable from a first retry.
	/// </summary>
	public static bool ShouldBeep(int attempt) => attempt > 1;

	/// <summary>
	/// How many beeps announce this attempt: one per attempt, so the listener can count
	/// how deep into the retries the operation is.
	/// </summary>
	public static int RepeatCountFor(int attempt) => ShouldBeep(attempt) ? attempt : 0;
}
