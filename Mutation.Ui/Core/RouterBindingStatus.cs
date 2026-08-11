using System;
using System.Collections.Generic;

namespace Mutation.Ui.Core;

/// <summary>
/// Works out what one row of the Hotkey Router table should say about itself, given the routes
/// the running app actually holds.
/// <para>
/// The Settings pages edit a copy of the settings and hand it over on save, so a row on screen
/// is not necessarily a route the app has. That is why a plain "bound or not" answer was never
/// going to be honest here: most rows on that page are neither, they are waiting. Comparing the
/// row against the app's live routes tells the three apart (issue #343).
/// </para>
/// </summary>
internal static class RouterBindingStatus
{
	/// <summary>
	/// The state of a row whose canonical "From" and "To" chords are
	/// <paramref name="normalizedFrom"/> and <paramref name="normalizedTo"/>.
	/// <para>
	/// A null <paramref name="liveRoutes"/> means the caller has no way to ask — nothing is
	/// registered yet, or this page has no connection to the running app — and the answer is
	/// <see cref="HotkeyBindingState.Unknown"/> rather than a guess. An empty list is a real
	/// answer: the app holds no routes, so a valid row is waiting for a save.
	/// </para>
	/// <para>
	/// Both halves of the mapping have to match, not just the shortcut being listened for.
	/// Changing only the "To" side leaves the same chord registered while what it sends is now
	/// out of date, and a row calling itself live on the strength of the "From" match alone
	/// would be telling the user their edit had taken effect when it had not.
	/// </para>
	/// </summary>
	internal static (HotkeyBindingState State, string? Message) For(
		string? normalizedFrom,
		string? normalizedTo,
		IReadOnlyList<RegisteredRouterRoute>? liveRoutes)
	{
		if (liveRoutes is null)
			return (HotkeyBindingState.Unknown, null);

		// Half a mapping is not a route. The row's own validation message already says what is
		// wrong with it, and a second line about not being live would be noise on top of it.
		if (string.IsNullOrWhiteSpace(normalizedFrom) || string.IsNullOrWhiteSpace(normalizedTo))
			return (HotkeyBindingState.Unknown, null);

		foreach (var route in liveRoutes)
		{
			if (!SameChord(route.From, normalizedFrom) || !SameChord(route.To, normalizedTo))
				continue;

			return route.Success
				? (HotkeyBindingState.Bound, null)
				: (HotkeyBindingState.Failed, route.ErrorMessage);
		}

		return (HotkeyBindingState.NotYetApplied, null);
	}

	/// <summary>
	/// Both sides are written in the app's one canonical spelling already — the rows canonicalise
	/// on commit, and what is on disk was written by a commit — so this is a comparison of two
	/// spellings that should agree, with the case and stray spaces forgiven in case one of them
	/// came from a hand-edited settings file.
	/// </summary>
	private static bool SameChord(string? left, string? right) =>
		string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
