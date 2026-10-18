using CognitiveSupport;
using System.Collections.Generic;

namespace Mutation.Ui.Services;

/// <summary>
/// Decides whether a Fast mode or reasoning fallback should be announced. The condition
/// that causes it (no research-preview access, Fast mode at capacity, a model that does not
/// accept the prompt's reasoning level) persists for as long as the user keeps running the
/// prompt, and repeating a long notice on every hotkey press is hostile to a screen-reader
/// user — so each prompt announces each distinct fallback at most once per app session.
///
/// Held in memory only, never persisted: access can be granted server-side at any time,
/// and a restart should tell the user again if it is still missing. Kept UI-free so the
/// suppression rule is unit-testable on its own.
/// </summary>
public sealed class FallbackNoticeTracker
{
	// One set for both kinds. The key carries the boxed reason or level, and an enum value
	// boxed from one type never equals one boxed from another, so a Fast mode notice
	// cannot use up a reasoning notice or the other way round.
	private readonly HashSet<(int PromptId, object Fallback)> _announced = new();
	private readonly object _gate = new();

	/// <summary>
	/// Returns true the first time this prompt hits this reason in the current session,
	/// and false every time after. Reason is part of the key so a capacity failure is
	/// still heard after an access failure was already announced — the two need different
	/// actions from the user, so silently swallowing the second would be misleading.
	/// Thread-safe: prompts run from hotkey threads as well as the UI thread.
	/// </summary>
	public bool ShouldAnnounce(int promptId, FastModeFallbackReason reason) => Claim(promptId, reason);

	/// <summary>
	/// Returns true the first time this model refuses this reasoning level for this prompt
	/// in the current session, and false every time after. The level and the model are both
	/// part of the key, so a prompt whose level or model was changed and refused again is
	/// heard again: the notice names the model, and staying quiet about a second model
	/// would leave the user believing it reasons. The model name is matched without regard
	/// to case, as the services match it.
	/// </summary>
	public bool ShouldAnnounce(int promptId, string modelName, ReasoningLevel refused) =>
		Claim(promptId, (modelName.ToUpperInvariant(), refused));

	/// <summary>
	/// The reasoning fallback notice to fold into this run's outcome line, or null when there
	/// was no fallback or it was already announced for this prompt, model and level. Shared by the
	/// prompt hotkey run and the run after dictation, so both word and limit it the same way.
	/// </summary>
	/// <param name="promptId">The prompt that ran.</param>
	/// <param name="modelName">The model it ran on, named in the notice.</param>
	/// <param name="refused">The level the model refused, or null when nothing was refused.</param>
	public string? ClaimReasoningNotice(int promptId, string modelName, ReasoningLevel? refused) =>
		refused is { } level && ShouldAnnounce(promptId, modelName, level)
			? ReasoningMessages.Fallback(modelName, level)
			: null;

	private bool Claim(int promptId, object fallback)
	{
		lock (_gate)
		{
			return _announced.Add((promptId, fallback));
		}
	}
}
