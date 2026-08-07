using System;
using System.Linq;
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

	// Issue #295. Cancelling has no transcript to put in the box, so the box kept the
	// "Transcribing..." the run had set — a screen reader read work as still running
	// long after the user had heard "Transcription cancelled."
	[Fact]
	public void Cancelled_TakesTheTranscribingPlaceholderBackOut()
	{
		var plan = RecordingUiPlanner.For(RecordingActivity.Cancelled);

		Assert.Equal(string.Empty, plan.TranscriptText);
		Assert.NotEqual(RecordingUiPlanner.TranscribingPlaceholder, plan.TranscriptText);
	}

	// Clearing the box is the only thing that separates a cancel from going idle: the
	// user is back where they started and everything must be theirs to drive again.
	[Fact]
	public void Cancelled_OtherwiseLooksExactlyLikeIdle()
	{
		var cancelled = RecordingUiPlanner.For(RecordingActivity.Cancelled);
		var idle = RecordingUiPlanner.For(RecordingActivity.Idle);

		Assert.Equal(idle.ButtonLabel, cancelled.ButtonLabel);
		Assert.Equal(idle.ButtonEnabled, cancelled.ButtonEnabled);
		Assert.Equal(idle.TranscriptReadOnly, cancelled.TranscriptReadOnly);
		Assert.Equal(idle.PlayStartBeep, cancelled.PlayStartBeep);
	}

	// Guards the other half of #295: clearing on a cancel must not turn into clearing on
	// the ordinary idle, which would wipe the transcript FinalizeTranscript just delivered.
	[Fact]
	public void OnlyCancelled_ClearsTheTranscriptBox()
	{
		Assert.Single(
			Enum.GetValues<RecordingActivity>(),
			a => RecordingUiPlanner.For(a).TranscriptText == string.Empty);
	}

	[Fact]
	public void OnlyRecording_SoundsTheStartBeep()
	{
		Assert.Single(Enum.GetValues<RecordingActivity>(), a => RecordingUiPlanner.For(a).PlayStartBeep);
	}

	// ----- The language-model step, added for issue #256.

	[Fact]
	public void ProcessingWithLlm_NamesItsOwnStep_NotTheOneThatHasFinished()
	{
		// Reported as Transcribing, the button and the box both said "Transcribing..." for
		// the whole model call — minutes of it — naming a step that had already finished.
		var plan = RecordingUiPlanner.For(RecordingActivity.ProcessingWithLlm);

		Assert.Equal("Processing with LLM...", plan.TranscriptText);
		Assert.NotEqual("Transcribing...", plan.TranscriptText);
		Assert.NotEqual("Transcribing...", plan.ButtonLabel);
		Assert.False(plan.PlayStartBeep);
		Assert.True(plan.TranscriptReadOnly);
	}

	[Fact]
	public void ProcessingWithLlm_LeavesItsButtonLive_SoTheCancelIsNotHotkeyOnly()
	{
		// The one busy state whose button stays enabled. Disabled, the cancel existed only
		// on the shortcut: a screen-reader user tabbing the window found a dimmed control
		// and no way to stop the longest wait in the flow (issue #256).
		var plan = RecordingUiPlanner.For(RecordingActivity.ProcessingWithLlm);

		Assert.True(plan.ButtonEnabled);
		Assert.Contains("Stop", plan.ButtonLabel);
	}

	// ----- Button descriptions, added for issue #309.

	[Fact]
	public void EveryBusyActivityDescribesItsButton_SoTheTooltipCannotGoStale()
	{
		// A busy state used to change only the accessible name. The tooltip went on saying
		// "Start or stop speech capture" while the button was renamed to something else, so
		// a screen-reader user and a sighted user hovering the same control were told
		// different things.
		Assert.False(string.IsNullOrWhiteSpace(
			RecordingUiPlanner.For(RecordingActivity.Transcribing).ButtonDescription));
		Assert.False(string.IsNullOrWhiteSpace(
			RecordingUiPlanner.For(RecordingActivity.ProcessingWithLlm).ButtonDescription));
	}

	[Fact]
	public void TheRestingActivitiesLeaveTheirDescriptionToTheHotkeyAffordances()
	{
		// Record and Stop get their tooltip from ConfigureButtonHotkey, which composes the
		// shortcut into it. A description here would fight that.
		Assert.Null(RecordingUiPlanner.For(RecordingActivity.Idle).ButtonDescription);
		Assert.Null(RecordingUiPlanner.For(RecordingActivity.Cancelled).ButtonDescription);
		Assert.Null(RecordingUiPlanner.For(RecordingActivity.Recording).ButtonDescription);
	}

	[Fact]
	public void ADescribedButtonSaysTheSameThingAsItsLabel()
	{
		// The stop and its description have to agree about what pressing it does, or the
		// tooltip becomes a second, quieter source of truth.
		var plan = RecordingUiPlanner.For(RecordingActivity.ProcessingWithLlm);

		Assert.Contains("Stop", plan.ButtonLabel);
		Assert.Contains("Stop", plan.ButtonDescription!);
	}

	[Fact]
	public void EveryActivityHasItsOwnButtonLabel()
	{
		// A label shared between two activities tells a screen-reader user the wrong thing
		// about at least one of them.
		var labels = Enum.GetValues<RecordingActivity>()
			.Select(a => RecordingUiPlanner.For(a).ButtonLabel)
			.ToList();

		// Idle and Cancelled are deliberately identical — Cancelled *is* idle, plus the
		// cleared box — so they count once between them.
		Assert.Equal(labels.Count - 1, labels.Distinct().Count());
	}
}
