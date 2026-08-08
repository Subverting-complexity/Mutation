namespace Mutation.Ui.Core;

/// <summary>
/// What happened when a microphone switch was applied.
/// </summary>
public enum MicrophoneSwitchOutcome
{
	/// <summary>The device was selected and capture was pointed at it.</summary>
	Switched,

	/// <summary>The device is no longer in the current enumeration — unplugged between the click and the switch.</summary>
	Unavailable,

	/// <summary>The device faulted while it was being selected or opened.</summary>
	Failed,
}

/// <summary>
/// Outcome of one microphone switch, together with the message to show when it
/// did not succeed. The message is carried here rather than composed by the
/// coordinator's caller from an exception it never saw, so the UI can report a
/// device fault without the switch having to run on the UI thread to throw there.
/// </summary>
/// <param name="Outcome">Which of the three ways the switch ended.</param>
/// <param name="FailureMessage">The device fault's message, set only for
/// <see cref="MicrophoneSwitchOutcome.Failed"/>.</param>
public readonly record struct MicrophoneSwitchResult(
	MicrophoneSwitchOutcome Outcome,
	string? FailureMessage = null)
{
	/// <summary>True when capture is now running on the requested device.</summary>
	public bool Switched => Outcome == MicrophoneSwitchOutcome.Switched;
}
