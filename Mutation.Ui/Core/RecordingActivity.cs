namespace Mutation.Ui.Core;

/// <summary>
/// What the audio session is doing, as the code that raises the change intends it —
/// not as a later reader of the recorder's own flags would see it.
/// </summary>
/// <remarks>
/// The distinction matters. The state change is delivered to the UI thread through the
/// dispatcher, so it arrives after the raising code has moved on. A handler that
/// re-read the recorder's flags could see a recording that had not finished stopping
/// yet and announce "Recording..." on a stop — telling a screen-reader user the exact
/// opposite of what they just did (issue #271). Carrying the intended activity with
/// the event removes the race: what was meant is what is announced.
/// </remarks>
public enum RecordingActivity
{
	/// <summary>Nothing in flight; the transcript box is the user's to edit.</summary>
	Idle,

	/// <summary>The microphone is live.</summary>
	Recording,

	/// <summary>Capture is over and the audio is being turned into text.</summary>
	Transcribing,
}
