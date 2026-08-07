namespace Mutation.Ui.Core;

/// <summary>
/// Turns a <see cref="RecordingActivity"/> into the window state it calls for. Pure, so
/// the announcement a screen-reader user gets for each activity can be asserted without
/// a window.
/// </summary>
public static class RecordingUiPlanner
{
	public const string RecordingPlaceholder = "Recording...";
	public const string TranscribingPlaceholder = "Transcribing...";

	public static RecordingUiPlan For(RecordingActivity activity) => activity switch
	{
		RecordingActivity.Recording => new RecordingUiPlan(
			ButtonLabel: "Stop",
			ButtonEnabled: true,
			TranscriptReadOnly: true,
			TranscriptText: RecordingPlaceholder,
			PlayStartBeep: true),

		RecordingActivity.Transcribing => new RecordingUiPlan(
			ButtonLabel: TranscribingPlaceholder,
			ButtonEnabled: false,
			TranscriptReadOnly: true,
			TranscriptText: TranscribingPlaceholder,
			PlayStartBeep: false),

		_ => new RecordingUiPlan(
			ButtonLabel: "Record",
			ButtonEnabled: true,
			TranscriptReadOnly: false,
			TranscriptText: null,
			PlayStartBeep: false),
	};
}
