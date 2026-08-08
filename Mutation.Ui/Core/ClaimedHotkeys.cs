using System;
using System.Collections.Generic;
using CognitiveSupport;

namespace Mutation.Ui.Core;

/// <summary>
/// Every shortcut the app has been configured with, named, gathered from one settings object.
/// <para>
/// <see cref="Services.HotkeyRegistrationTable"/> is a single table for the whole app, so the
/// three lists that fill it — the built-in shortcuts, the router's "From" chords, and the
/// shortcut on each LLM prompt — compete for the same chords. Anything asking "is this one
/// taken" has to see all three, and the prompt editor can see none of them on its own screen
/// (issue #340).
/// </para>
/// </summary>
internal static class ClaimedHotkeys
{
	/// <summary>
	/// What one router mapping is called in a sentence. A route has no name of its own, and it
	/// does not need one: its "From" chord is the chord being complained about, and the row
	/// holding it is on the Hotkeys page where the user is being sent anyway.
	/// </summary>
	public const string RouteName = "a hotkey route";

	/// <summary>
	/// Everything except <paramref name="excludedPrompt"/>, which is the prompt being edited —
	/// left out so its own saved shortcut is not reported as a clash with itself.
	/// </summary>
	public static IReadOnlyList<NamedHotkey> Excluding(Settings settings, LlmSettings.LlmPrompt? excludedPrompt)
	{
		if (settings is null) throw new ArgumentNullException(nameof(settings));

		var claimed = new List<NamedHotkey>();

		foreach (var spec in CoreHotkeys.All)
			claimed.Add(new(spec.SpokenName, spec.Getter(settings), spec.Registers));

		var mappings = settings.HotKeyRouterSettings?.Mappings;
		if (mappings is not null)
		{
			foreach (var mapping in mappings)
				claimed.Add(new(RouteName, mapping.FromHotKey, ClaimsTheChord: true));
		}

		var prompts = settings.LlmSettings?.Prompts;
		if (prompts is not null)
		{
			foreach (var prompt in prompts)
			{
				// By reference, not by name: two prompts may share a name, and the one being
				// edited is the object the editor was handed.
				if (ReferenceEquals(prompt, excludedPrompt))
					continue;

				claimed.Add(new(NameOf(prompt), prompt.Hotkey, ClaimsTheChord: true));
			}
		}

		return claimed;
	}

	/// <summary>
	/// How a prompt is referred to in a clash message. Quoted, because a prompt's name is the
	/// user's own words and could be anything — an unquoted "Already used by the prompt do the
	/// thing." reads as a sentence that lost its ending.
	/// </summary>
	public static string NameOf(LlmSettings.LlmPrompt prompt)
	{
		string name = prompt?.Name ?? string.Empty;
		return string.IsNullOrWhiteSpace(name) ? "an unnamed LLM prompt" : $"the LLM prompt \"{name.Trim()}\"";
	}
}
