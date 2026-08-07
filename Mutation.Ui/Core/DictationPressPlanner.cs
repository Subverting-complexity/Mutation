namespace Mutation.Ui.Core;

/// <summary>
/// What a press of a dictation shortcut means, given what the session is already doing.
/// </summary>
public enum DictationPressAction
{
	/// <summary>Nothing in flight: open the microphone.</summary>
	StartRecording,

	/// <summary>The microphone is live: close it and transcribe what was captured.</summary>
	StopAndTranscribe,

	/// <summary>A transcription is running: ask it to give up.</summary>
	CancelTranscription,

	/// <summary>A transcription has already been asked to give up and is winding down.</summary>
	TranscriptionAlreadyStopping,

	/// <summary>A language-model call is running: ask it to give up.</summary>
	CancelLlmProcessing,

	/// <summary>A language-model call has already been asked to give up.</summary>
	LlmAlreadyStopping,
}

/// <summary>
/// Decides what one press of a dictation shortcut does. Pure, so the two orderings that
/// matter can be asserted without a window.
/// </summary>
/// <remarks>
/// <para>
/// The language-model check has to come before the recording check. Transcription ends and
/// disposes its cancellation source before the prompt is sent, so during the model call
/// the recorder reports neither recording nor transcribing — and a press read as "nothing
/// in flight" opened a fresh recording on top of the running request, which then delivered
/// a transcript and reset the state over the top of the new recording (issue #256).
/// </para>
/// <para>
/// The "already stopping" answers exist because cancelling is not instant: the call
/// unwinds when the HTTP read it is waiting on lets go, which can take seconds. A user who
/// hears nothing presses again, and re-announcing the request meant hearing the same
/// sentence twice — the complaint issue #299 was filed about.
/// </para>
/// </remarks>
public static class DictationPressPlanner
{
	public static DictationPressAction For(
		bool isTranscribing,
		bool transcriptionCancelRequested,
		bool isProcessingWithLlm,
		bool llmCancelRequested,
		bool isRecording)
	{
		if (isTranscribing)
			return transcriptionCancelRequested
				? DictationPressAction.TranscriptionAlreadyStopping
				: DictationPressAction.CancelTranscription;

		// Before the recording test. See the remarks above — this is the whole fix for the
		// recording that used to start underneath a running model call.
		if (isProcessingWithLlm)
			return llmCancelRequested
				? DictationPressAction.LlmAlreadyStopping
				: DictationPressAction.CancelLlmProcessing;

		return isRecording
			? DictationPressAction.StopAndTranscribe
			: DictationPressAction.StartRecording;
	}
}
