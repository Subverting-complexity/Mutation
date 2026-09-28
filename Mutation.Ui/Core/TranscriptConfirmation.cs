using System;
using CognitiveSupport;

namespace Mutation.Ui.Core;

/// <summary>
/// The audible end of a transcript run — the shortcut sent afterwards and the beep — made the
/// moment the delivery outcome is known, on whichever thread learns it first, and made once.
/// <para>
/// This exists because of what the delivery timeline showed (issue #411). The paste is sent from
/// a background thread and Windows accepted it in 5 ms, but the UI thread did not take the work
/// back for 4.8 seconds, and the beep and the shortcut were both queued behind it. The user heard
/// the text land and then nothing, for seconds or minutes, because the window was busy with
/// something the confirmation never needed. Neither the beep nor the shortcut touches the window,
/// so neither has any reason to wait for it.
/// </para>
/// <para>
/// Once, because the insert path confirms from the background thread as soon as
/// <c>SendInput</c> returns, and the caller confirms again on the UI thread for the paths that
/// never leave it (nothing to paste, Mutation itself in front, an elevated window). The second
/// call hands back the first plan and does nothing else, so a beep or a shortcut can never be
/// doubled — a doubled shortcut would run the user's command twice.
/// </para>
/// </summary>
internal sealed class TranscriptConfirmation
{
	private readonly bool _clipboardCopied;
	private readonly string _subject;
	private readonly Action<BeepType> _playBeep;
	private readonly Action _sendHotkey;
	private readonly object _gate = new();
	private TranscriptCompletionPlan? _plan;

	/// <param name="clipboardCopied">Whether the text reached the clipboard.</param>
	/// <param name="subject">Names the text in a failure message — "transcript", "processed text".</param>
	/// <param name="playBeep">Plays the beep. Must be safe to call from any thread.</param>
	/// <param name="sendHotkey">Sends the configured shortcut. Must be safe to call from any thread.</param>
	public TranscriptConfirmation(bool clipboardCopied, string subject, Action<BeepType> playBeep, Action sendHotkey)
	{
		_clipboardCopied = clipboardCopied;
		_subject = subject ?? throw new ArgumentNullException(nameof(subject));
		_playBeep = playBeep ?? throw new ArgumentNullException(nameof(playBeep));
		_sendHotkey = sendHotkey ?? throw new ArgumentNullException(nameof(sendHotkey));
	}

	/// <summary>
	/// Decides the end of the run from <paramref name="outcome"/>, and on the first call sends the
	/// shortcut and plays the beep. Every later call returns the first plan and does nothing.
	/// </summary>
	public TranscriptCompletionPlan Confirm(TranscriptDeliveryOutcome outcome)
	{
		TranscriptCompletionPlan plan;
		lock (_gate)
		{
			if (_plan is { } decided)
				return decided;

			plan = TranscriptCompletionPlanner.Plan(_clipboardCopied, outcome, _subject);
			_plan = plan;
		}

		DeliveryTrace.Write(
			$"Delivery confirmed ({outcome}): shortcut {(plan.SendConfiguredHotkey ? "sent" : "not sent")}, {plan.Beep} beep played, without waiting for the UI thread.");

		// The shortcut ahead of the beep, as before: the beep is the step more likely to throw,
		// and the text has already landed, so the shortcut that acts on it should not be the
		// thing that is lost (issue #335). Outside the lock so a slow beep holds nobody up.
		if (plan.SendConfiguredHotkey)
			_sendHotkey();

		_playBeep(plan.Beep);
		return plan;
	}
}
