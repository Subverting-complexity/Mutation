using Mutation.Ui.Core;

namespace Mutation.Tests;

// The two orderings a press of a dictation shortcut has to get right, asserted without a
// window. Both were bugs: the model call being invisible to the press (issue #256) and a
// repeat press re-announcing the request it had already made (issue #299).
public class DictationPressPlannerTests
{
	private static DictationPressAction For(
		bool isTranscribing = false,
		bool transcriptionCancelRequested = false,
		bool isProcessingWithLlm = false,
		bool llmCancelRequested = false,
		bool isRecording = false)
		=> DictationPressPlanner.For(
			isTranscribing,
			transcriptionCancelRequested,
			isProcessingWithLlm,
			llmCancelRequested,
			isRecording);

	// ----- The ordinary loop.

	[Fact]
	public void NothingInFlight_StartsRecording()
		=> Assert.Equal(DictationPressAction.StartRecording, For());

	[Fact]
	public void Recording_StopsAndTranscribes()
		=> Assert.Equal(DictationPressAction.StopAndTranscribe, For(isRecording: true));

	// ----- Cancelling.

	[Fact]
	public void Transcribing_CancelsTheTranscription()
		=> Assert.Equal(DictationPressAction.CancelTranscription, For(isTranscribing: true));

	[Fact]
	public void ProcessingWithLlm_CancelsTheModelCall()
		=> Assert.Equal(DictationPressAction.CancelLlmProcessing, For(isProcessingWithLlm: true));

	// ----- The ordering that is the whole fix for issue #256.

	[Fact]
	public void ProcessingWithLlm_IsCheckedBeforeTheRecorder_SoNoRecordingStartsUnderTheCall()
	{
		// The recorder reports idle during the model call: the transcription ends and
		// disposes its cancellation source before the prompt is sent. A press read as
		// "nothing in flight" opened a fresh recording on top of the running request, which
		// then delivered a transcript and reset the state over the new recording.
		Assert.Equal(
			DictationPressAction.CancelLlmProcessing,
			For(isProcessingWithLlm: true, isRecording: false));
	}

	[Fact]
	public void Transcribing_WinsOverEverythingElse()
	{
		// Belt and braces: if both ever read true, the transcription owns the session state
		// and is the one that has to be told to stop.
		Assert.Equal(
			DictationPressAction.CancelTranscription,
			For(isTranscribing: true, isProcessingWithLlm: true, isRecording: true));
	}

	// ----- Repeat presses. Issue #299 was hearing the same sentence twice.

	[Fact]
	public void TranscriptionAlreadyStopping_IsAnsweredDifferentlyFromTheFirstPress()
	{
		Assert.Equal(
			DictationPressAction.TranscriptionAlreadyStopping,
			For(isTranscribing: true, transcriptionCancelRequested: true));
	}

	[Fact]
	public void LlmAlreadyStopping_IsAnsweredDifferentlyFromTheFirstPress()
	{
		Assert.Equal(
			DictationPressAction.LlmAlreadyStopping,
			For(isProcessingWithLlm: true, llmCancelRequested: true));
	}

	[Fact]
	public void AStopAlreadyAskedFor_IsNeverSilent()
	{
		// A shortcut that produces nothing reads as one that did not register — the reason
		// the OCR Cancel button answers a repeat press rather than ignoring it (issue #227).
		Assert.NotEqual(
			DictationPressAction.StartRecording,
			For(isProcessingWithLlm: true, llmCancelRequested: true));
		Assert.NotEqual(
			DictationPressAction.StartRecording,
			For(isTranscribing: true, transcriptionCancelRequested: true));
	}

	[Fact]
	public void ACancelRequestFlagWithNothingRunning_IsIgnored()
	{
		// The flags are only meaningful while their operation is live; a stale one must not
		// stop a press from starting a recording.
		Assert.Equal(
			DictationPressAction.StartRecording,
			For(transcriptionCancelRequested: true, llmCancelRequested: true));
	}
}
