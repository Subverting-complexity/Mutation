using Mutation.Ui.Core;

namespace Mutation.Tests;

// Covers issue #271: the state-changed handler branched on the recorder's IsRecording
// flag, which it read after the dispatcher had handed it back to the UI thread. When
// the stop path had not finished stopping by then, the handler took the *start* branch
// on a stop — start beep, Stop button, "Recording..." — telling a screen-reader user
// that recording had just begun a moment after they ended it.
//
// The activity now travels with the event, and this planner is what turns it into what
// the user sees and hears. Nothing here reads recorder state, so a stop cannot be
// announced as a start no matter how the threads interleave.
public class RecordingUiPlannerTests
{
	[Fact]
	public void Recording_StartsWithTheStartBeep()
	{
		var plan = RecordingUiPlanner.For(RecordingActivity.Recording);

		Assert.True(plan.PlayStartBeep);
		Assert.Equal("Stop", plan.ButtonLabel);
		Assert.True(plan.ButtonEnabled);
		Assert.True(plan.TranscriptReadOnly);
		Assert.Equal("Recording...", plan.TranscriptText);
	}

	// The heart of #271: whatever the recorder's flags happen to say while a stop is
	// still unwinding, the transcribing activity never sounds the start beep and never
	// puts the button into its Stop state.
	[Fact]
	public void Transcribing_NeverSoundsTheStartBeep_AndNeverReadsAsRecording()
	{
		var plan = RecordingUiPlanner.For(RecordingActivity.Transcribing);

		Assert.False(plan.PlayStartBeep);
		Assert.Equal("Transcribing...", plan.ButtonLabel);
		Assert.NotEqual("Stop", plan.ButtonLabel);
		Assert.Equal("Transcribing...", plan.TranscriptText);
		Assert.True(plan.TranscriptReadOnly);
	}

	// The buttons go dead while the audio is being turned into text, so a second press
	// cannot queue another operation behind the one already running.
	[Fact]
	public void Transcribing_DisablesTheRecordButtons()
	{
		Assert.False(RecordingUiPlanner.For(RecordingActivity.Transcribing).ButtonEnabled);
	}

	[Fact]
	public void Idle_HandsTheTranscriptBoxBack_AndIsSilent()
	{
		var plan = RecordingUiPlanner.For(RecordingActivity.Idle);

		Assert.False(plan.PlayStartBeep);
		Assert.Equal("Record", plan.ButtonLabel);
		Assert.True(plan.ButtonEnabled);
		Assert.False(plan.TranscriptReadOnly);
	}

	// Going idle must not wipe the transcript that has just been delivered into the box.
	[Fact]
	public void Idle_LeavesTheTranscriptTextAlone()
	{
		Assert.Null(RecordingUiPlanner.For(RecordingActivity.Idle).TranscriptText);
	}

	[Fact]
	public void OnlyRecording_SoundsTheStartBeep()
	{
		Assert.Single(
			new[] { RecordingActivity.Idle, RecordingActivity.Recording, RecordingActivity.Transcribing },
			a => RecordingUiPlanner.For(a).PlayStartBeep);
	}
}
