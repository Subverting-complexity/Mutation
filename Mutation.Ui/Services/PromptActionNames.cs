namespace Mutation.Ui.Services;

/// <summary>
/// Builds the accessible name for a per-prompt action button.
/// <para>
/// The Run, Edit and Delete buttons in the prompt list carry the same three words on every
/// row, so arrowing down the list a screen reader said "Delete" over and over with nothing to
/// say which prompt it belonged to (issue #243). The name has to carry the row.
/// </para>
/// </summary>
internal static class PromptActionNames
{
	/// <summary>
	/// For example <c>Build("Delete", "Summarise")</c> → "Delete prompt 'Summarise'". A row
	/// whose prompt has no name still gets a whole sentence: "Delete this prompt".
	/// </summary>
	internal static string Build(string? verb, string? promptName)
	{
		string subject = PromptLabel.Describe(promptName);

		return string.IsNullOrWhiteSpace(verb)
			? subject
			: $"{verb.Trim()} {subject}";
	}
}
