using CognitiveSupport;
using System.Collections.Generic;

namespace Mutation.Ui.Services;

/// <summary>
/// Decides whether a Fast mode fallback should be announced. The condition that causes
/// it (no research-preview access, or Fast mode at capacity) persists for as long as the
/// user keeps running the prompt, and repeating a long verbose notice on every hotkey
/// press is hostile to a screen-reader user — so each prompt announces each distinct
/// reason at most once per app session.
///
/// Held in memory only, never persisted: access can be granted server-side at any time,
/// and a restart should tell the user again if it is still missing. Kept UI-free so the
/// suppression rule is unit-testable on its own.
/// </summary>
public sealed class FastModeNoticeTracker
{
	private readonly HashSet<(int PromptId, FastModeFallbackReason Reason)> _announced = new();
	private readonly object _gate = new();

	/// <summary>
	/// Returns true the first time this prompt hits this reason in the current session,
	/// and false every time after. Reason is part of the key so a capacity failure is
	/// still heard after an access failure was already announced — the two need different
	/// actions from the user, so silently swallowing the second would be misleading.
	/// Thread-safe: prompts run from hotkey threads as well as the UI thread.
	/// </summary>
	public bool ShouldAnnounce(int promptId, FastModeFallbackReason reason)
	{
		lock (_gate)
		{
			return _announced.Add((promptId, reason));
		}
	}

	/// <summary>Forgets every announcement, as if the session had just started.</summary>
	public void Reset()
	{
		lock (_gate)
		{
			_announced.Clear();
		}
	}
}
