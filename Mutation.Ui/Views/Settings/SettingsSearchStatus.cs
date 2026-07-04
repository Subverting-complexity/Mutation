namespace Mutation.Ui.Views.SettingsUi;

// Composes the search-status text shown under the settings search box and
// announced through its live region. Pure logic, no UI types, so it is
// unit-testable in isolation (same pattern as StatusAnnouncement).
public static class SettingsSearchStatus
{
	/// <summary>
	/// Builds the status line for the current search state.
	/// Returns an empty string when the query is empty (nothing to report).
	/// </summary>
	/// <param name="query">The raw search box text.</param>
	/// <param name="matchingCategories">Categories whose keywords match the query.</param>
	/// <param name="totalCategories">Total number of settings categories.</param>
	/// <param name="matchingSections">Sections on the active page that match, or null when no page is loaded yet.</param>
	public static string Compose(string? query, int matchingCategories, int totalCategories, int? matchingSections)
	{
		if (string.IsNullOrWhiteSpace(query))
			return string.Empty;

		if (matchingCategories == 0)
			return "No categories match.";

		string categories = $"{matchingCategories} of {totalCategories} categories match.";

		if (matchingSections is null)
			return categories;

		string sections = matchingSections == 1
			? "1 matching section shown on this page."
			: $"{matchingSections} matching sections shown on this page.";

		return $"{categories} {sections}";
	}
}
