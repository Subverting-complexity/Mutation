using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;
using Mutation.Ui.Core;

namespace Mutation.Tests;

/// <summary>
/// The beep and the shortcut at the end of a transcript run, made the moment the outcome is
/// known rather than when the UI thread gets round to it (issue #411).
/// <para>
/// The insert path confirms from a background thread as soon as the paste is accepted, and the
/// caller confirms again on the UI thread for the paths that never leave it. So the rule that
/// matters most is that only the first confirmation acts: a second beep is noise, and a second
/// shortcut runs the user's command twice.
/// </para>
/// </summary>
public class TranscriptConfirmationTests
{
	private sealed class Recorder
	{
		public List<string> Calls { get; } = new();

		public TranscriptConfirmation Create(bool clipboardCopied = true) =>
			new(clipboardCopied, "transcript",
				beep => { lock (Calls) Calls.Add($"beep:{beep}"); },
				() => { lock (Calls) Calls.Add("hotkey"); });
	}

	[Fact]
	public void A_delivered_transcript_sends_the_shortcut_and_then_plays_the_success_beep()
	{
		var recorder = new Recorder();

		recorder.Create().Confirm(TranscriptDeliveryOutcome.Delivered);

		Assert.Equal(new[] { "hotkey", $"beep:{BeepType.Success}" }, recorder.Calls);
	}

	[Fact]
	public void A_failed_delivery_plays_the_failure_beep_and_sends_no_shortcut()
	{
		var recorder = new Recorder();

		var plan = recorder.Create().Confirm(TranscriptDeliveryOutcome.InjectionFailed);

		Assert.Equal(new[] { $"beep:{BeepType.Failure}" }, recorder.Calls);
		Assert.False(plan.Succeeded);
	}

	[Fact]
	public void A_second_confirmation_does_nothing_and_returns_the_first_plan()
	{
		var recorder = new Recorder();
		var confirmation = recorder.Create();

		var first = confirmation.Confirm(TranscriptDeliveryOutcome.Delivered);
		// The caller's own confirmation, arriving after the background thread's. It carries the
		// same outcome in practice; a different one proves the first decision is the one kept.
		var second = confirmation.Confirm(TranscriptDeliveryOutcome.InjectionFailed);

		Assert.Equal(first, second);
		Assert.Equal(2, recorder.Calls.Count);
	}

	[Fact]
	public async Task Confirmations_racing_from_two_threads_still_act_only_once()
	{
		for (var run = 0; run < 50; run++)
		{
			var recorder = new Recorder();
			var confirmation = recorder.Create();
			using var start = new ManualResetEventSlim(false);

			var a = Task.Run(() => { start.Wait(); confirmation.Confirm(TranscriptDeliveryOutcome.Delivered); });
			var b = Task.Run(() => { start.Wait(); confirmation.Confirm(TranscriptDeliveryOutcome.Delivered); });
			start.Set();
			await Task.WhenAll(a, b);

			lock (recorder.Calls)
				Assert.Equal(new[] { "hotkey", $"beep:{BeepType.Success}" }, recorder.Calls);
		}
	}

	[Fact]
	public void A_clipboard_that_was_not_set_is_part_of_the_decision()
	{
		var recorder = new Recorder();

		var plan = recorder.Create(clipboardCopied: false).Confirm(TranscriptDeliveryOutcome.Delivered);

		Assert.Equal(BeepType.Failure, plan.Beep);
		Assert.False(plan.SendConfiguredHotkey);
	}
}
