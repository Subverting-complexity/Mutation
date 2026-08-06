namespace CognitiveSupport;

/// <summary>
/// Makes a hotkey-router mapping list safe to hand to the rest of the app, and says
/// what it had to change.
///
/// A settings file can carry <c>"FromHotKey": null</c>, a mapping object with the key
/// missing entirely, or a null entry in the list — all easy to produce by hand-editing
/// <c>Mutation.json</c>. None of those should cost the user every other setting in the
/// file, so they are repaired here rather than thrown on (issue #247).
/// </summary>
public static class HotKeyRouterMappingRepair
{
	/// <summary>
	/// Replaces null hotkeys with blanks and removes null entries, in place.
	/// Returns one message per repair, for the caller to show the user; empty when
	/// there was nothing to fix.
	/// </summary>
	/// <remarks>
	/// A mapping that is merely blank on one or both sides is left alone and not
	/// reported: the Hotkeys page adds exactly that when the user clicks Add, and the
	/// router already treats an incomplete mapping as inactive.
	/// </remarks>
	public static IReadOnlyList<string> Repair(List<HotKeyRouterSettings.HotKeyRouterMap>? mappings)
	{
		if (mappings is null)
			return Array.Empty<string>();

		var issues = new List<string>();
		var repaired = new List<HotKeyRouterSettings.HotKeyRouterMap>(mappings.Count);

		for (int i = 0; i < mappings.Count; i++)
		{
			// Position in the original file is how the user identifies a row on the
			// Hotkeys page; a mapping has no other name to give them.
			int position = i + 1;
			var mapping = mappings[i];

			if (mapping is null)
			{
				issues.Add($"Hotkey router mapping {position} was empty and has been removed.");
				continue;
			}

			if (mapping.FromHotKey is null)
			{
				mapping.FromHotKey = string.Empty;
				issues.Add($"Hotkey router mapping {position} had no 'from' hotkey. It has been left blank for you to fill in.");
			}

			if (mapping.ToHotKey is null)
			{
				mapping.ToHotKey = string.Empty;
				issues.Add($"Hotkey router mapping {position} had no 'to' hotkey. It has been left blank for you to fill in.");
			}

			repaired.Add(mapping);
		}

		if (repaired.Count != mappings.Count)
		{
			// In place: callers (and the settings graph) hold this list instance.
			mappings.Clear();
			mappings.AddRange(repaired);
		}

		return issues;
	}
}
