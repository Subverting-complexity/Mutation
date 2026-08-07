namespace Mutation.Ui.Core;

/// <summary>
/// What the main window should look like, and sound like, for one recording activity.
/// </summary>
/// <param name="ButtonLabel">Accessible label for the record/stop buttons.</param>
/// <param name="ButtonDescription">
/// Tooltip and help text for those buttons while this activity is running, or null for
/// the resting states, whose description comes from the hotkey affordances instead.
/// Stated here because a busy state used to change only the accessible name: the tooltip
/// went on claiming "Start or stop speech capture" while the button was renamed to
/// something else entirely, so a screen-reader user and a sighted user hovering the same
/// control were told different things (issue #309).
/// </param>
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
	string? ButtonDescription,
	bool ButtonEnabled,
	bool TranscriptReadOnly,
	string? TranscriptText,
	bool PlayStartBeep);
