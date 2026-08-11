using System;

namespace Mutation.Ui.Core;

/// <summary>
/// Whether one section of a settings page answers what the user typed into the search box.
/// <para>
/// Split out of the tree walk in <c>SettingsSearchHelper</c>, which gathers a section's text
/// out of the live visual tree and then reports its answer by collapsing the section. The
/// gathering and the collapsing both need a real WinUI tree; deciding matched or not does
/// not, and it is the part with a rule in it (issue #304).
/// </para>
/// </summary>
internal static class SettingsSectionMatcher
{
	/// <summary>
	/// True when the search box is effectively empty — nothing typed, or only spaces. Every
	/// section matches an empty query, so the caller shows the whole page rather than gathering
	/// any text at all.
	/// </summary>
	internal static bool IsEmptyQuery(string? query) => Normalize(query).Length == 0;

	/// <summary>
	/// True when <paramref name="sectionText"/> contains <paramref name="query"/>, ignoring
	/// case and surrounding spaces. A section with no text at all matches only an empty query.
	/// <para>
	/// Case is folded with the invariant culture, matching what the tree walk did before this
	/// was split out. It means the match does not follow the user's locale, which for a query
	/// against English setting labels is the behaviour that stays predictable — Turkish
	/// lower-casing an "I" would stop "Interface" answering a typed "i".
	/// </para>
	/// </summary>
	internal static bool Matches(string? sectionText, string? query)
	{
		string needle = Normalize(query);
		if (needle.Length == 0)
			return true;

		return (sectionText ?? string.Empty).ToLowerInvariant().Contains(needle, StringComparison.Ordinal);
	}

	private static string Normalize(string? query) =>
		(query ?? string.Empty).Trim().ToLowerInvariant();
}
