namespace Mutation.Ui.Core;

/// <summary>
/// What the main window should look like, and sound like, for one recording activity.
/// </summary>
/// <param name="ButtonLabel">Accessible label for the record/stop buttons.</param>
/// <param name="ButtonEnabled">Whether those buttons can be pressed.</param>
/// <param name="TranscriptReadOnly">Whether the raw transcript box accepts typing.</param>
/// <param name="TranscriptText">
/// Placeholder to show in the raw transcript box, or null to leave whatever is there.
/// </param>
/// <param name="PlayStartBeep">
/// Whether to sound the start beep. Only a genuine start does — the beep is the blind
/// user's confirmation that the microphone is live, so it must never accompany a stop.
/// </param>
public sealed record RecordingUiPlan(
	string ButtonLabel,
	bool ButtonEnabled,
	bool TranscriptReadOnly,
	string? TranscriptText,
	bool PlayStartBeep);
