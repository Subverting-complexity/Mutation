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
	public const string ProcessingWithLlmPlaceholder = "Processing with LLM...";

	public static RecordingUiPlan For(RecordingActivity activity) => activity switch
	{
		RecordingActivity.Recording => new RecordingUiPlan(
			ButtonLabel: "Stop",
			ButtonDescription: null,
			ButtonEnabled: true,
			TranscriptReadOnly: true,
			TranscriptText: RecordingPlaceholder,
			PlayStartBeep: true),

		RecordingActivity.Transcribing => new RecordingUiPlan(
			ButtonLabel: TranscribingPlaceholder,
			ButtonDescription: "Turning your recording into text. Wait for it to finish.",
			ButtonEnabled: false,
			TranscriptReadOnly: true,
			TranscriptText: TranscribingPlaceholder,
			PlayStartBeep: false),

		// The one busy state that leaves its button live. The model call is the longest
		// wait in the flow and the only one the user may well want out of, so the button
		// that started it stays enabled and says what pressing it now does — otherwise the
		// cancel exists only on the shortcut (issue #256). The box names the step that is
		// really running rather than leaving "Transcribing..." standing over it.
		RecordingActivity.ProcessingWithLlm => new RecordingUiPlan(
			ButtonLabel: "Stop LLM processing",
			ButtonDescription: "Stop the language model and keep the transcript as it is",
			ButtonEnabled: true,
			TranscriptReadOnly: true,
			TranscriptText: ProcessingWithLlmPlaceholder,
			PlayStartBeep: false),

		// Idle in every respect, except that the placeholder is cleared. Nothing was
		// delivered into the box, so what is in it is the "Transcribing..." this run
		// put there — and a screen reader would read that back as work still running.
		RecordingActivity.Cancelled => new RecordingUiPlan(
			ButtonLabel: "Record",
			ButtonDescription: null,
			ButtonEnabled: true,
			TranscriptReadOnly: false,
			TranscriptText: string.Empty,
			PlayStartBeep: false),

		_ => new RecordingUiPlan(
			ButtonLabel: "Record",
			ButtonDescription: null,
			ButtonEnabled: true,
			TranscriptReadOnly: false,
			TranscriptText: null,
			PlayStartBeep: false),
	};
}
