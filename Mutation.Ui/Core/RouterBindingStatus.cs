using Mutation.Ui.Services;
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
	/// Whether two spellings name the same chord. Both sides go through
	/// <see cref="Hotkey.Canonicalize"/> first, which is the app's single authority on how a
	/// chord is spelled, so <c>CONTROL+SHIFT+ALT+8</c> and <c>CTRL+SHIFT+ALT+8</c> are one
	/// shortcut and <c>SHIFT+CTRL+A</c> and <c>CTRL+SHIFT+A</c> are one shortcut.
	/// <para>
	/// Comparing the raw text instead was a real bug, not a theoretical one. The row's side has
	/// been through <c>Canonicalize</c>; the live route's side is whatever is in the settings
	/// file. A brand-new settings file is seeded with a router mapping written
	/// <c>CONTROL+SHIFT+ALT+8</c>, so on a fresh install the one router row on the page reported
	/// a working mapping as "not active yet" — the same class of false statement issue #343 was
	/// filed about. Any file written before the canonicalisation work, or edited by hand, does
	/// the same.
	/// </para>
	/// <para>
	/// This is the lesson <see cref="HotkeyConflictFinder"/> already learned for duplicate
	/// detection (issue #306): a shortcut has one identity and many spellings, and comparing
	/// spellings gets it wrong.
	/// </para>
	/// </summary>
	private static bool SameChord(string? left, string? right) =>
		string.Equals(
			Hotkey.Canonicalize(left)?.Trim(),
			Hotkey.Canonicalize(right)?.Trim(),
			StringComparison.OrdinalIgnoreCase);
}
