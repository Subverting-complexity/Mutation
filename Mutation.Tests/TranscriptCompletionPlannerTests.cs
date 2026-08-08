using CognitiveSupport;
using Mutation.Ui.Core;

namespace Mutation.Tests;

/// <summary>
/// What the user hears and what runs when a transcript finishes.
/// <para>
/// Issue #325: a delivery that had in fact landed was reported as failed, so dictation
/// ended on the failure beep and the shortcut configured to run afterwards was skipped.
/// Both came from one decision, and it was three lines inline in two handlers where
/// nothing could reach it.
/// </para>
/// </summary>
public class TranscriptCompletionPlannerTests
{
	[Fact]
	public void Delivered_PlaysTheSuccessBeep()
	{
		var plan = TranscriptCompletionPlanner.Plan(true, TranscriptDeliveryOutcome.Delivered, "transcript");

		Assert.Equal(BeepType.Success, plan.Beep);
	}

	[Fact]
	public void Delivered_SendsTheConfiguredHotkey()
	{
		var plan = TranscriptCompletionPlanner.Plan(true, TranscriptDeliveryOutcome.Delivered, "transcript");

		Assert.True(plan.SendConfiguredHotkey);
	}

	[Fact]
	public void Delivered_SaysNothingWentWrong()
	{
		var plan = TranscriptCompletionPlanner.Plan(true, TranscriptDeliveryOutcome.Delivered, "transcript");

		Assert.Null(plan.FailureMessage);
		Assert.True(plan.Succeeded);
	}

	[Theory]
	[InlineData(true, TranscriptDeliveryOutcome.InjectionFailed)]
	[InlineData(false, TranscriptDeliveryOutcome.InjectionFailed)]
	[InlineData(false, TranscriptDeliveryOutcome.Delivered)]
	[InlineData(true, TranscriptDeliveryOutcome.ClipboardBlocked)]
	public void EveryFailure_PlaysTheFailureBeep(bool copied, TranscriptDeliveryOutcome outcome)
	{
		var plan = TranscriptCompletionPlanner.Plan(copied, outcome, "transcript");

		Assert.Equal(BeepType.Failure, plan.Beep);
		Assert.False(plan.Succeeded);
	}

	[Theory]
	[InlineData(true, TranscriptDeliveryOutcome.InjectionFailed)]
	[InlineData(false, TranscriptDeliveryOutcome.Delivered)]
	[InlineData(true, TranscriptDeliveryOutcome.ClipboardBlocked)]
	public void AFailure_DoesNotSendTheConfiguredHotkey(bool copied, TranscriptDeliveryOutcome outcome)
	{
		// That shortcut is whatever the user does next with the text. Firing it after a
		// delivery that did not land aims it at whatever was there before.
		var plan = TranscriptCompletionPlanner.Plan(copied, outcome, "transcript");

		Assert.False(plan.SendConfiguredHotkey);
	}

	[Fact]
	public void AFailure_SaysWhatWentWrongAndWhereTheTextIs()
	{
		var plan = TranscriptCompletionPlanner.Plan(true, TranscriptDeliveryOutcome.InjectionFailed, "transcript");

		Assert.NotNull(plan.FailureMessage);
		Assert.Contains("transcript", plan.FailureMessage);
		Assert.Contains(TranscriptDeliveryMessages.StillAvailable, plan.FailureMessage);
	}

	[Fact]
	public void TheSubjectNamesTheTextInTheMessage()
	{
		var plan = TranscriptCompletionPlanner.Plan(true, TranscriptDeliveryOutcome.InjectionFailed, "processed text");

		Assert.Contains("processed text", plan.FailureMessage);
	}

	[Fact]
	public void TheBeepAndTheHotkeyNeverDisagree()
	{
		// One decision, so the success beep and the shortcut can only be lost together —
		// never the beep saying it worked while the shortcut is skipped, or the reverse.
		foreach (var outcome in Enum.GetValues<TranscriptDeliveryOutcome>())
		{
			foreach (var copied in new[] { true, false })
			{
				var plan = TranscriptCompletionPlanner.Plan(copied, outcome, "transcript");

				Assert.Equal(plan.Beep == BeepType.Success, plan.SendConfiguredHotkey);
				Assert.Equal(plan.Succeeded, plan.SendConfiguredHotkey);
			}
		}
	}
}
