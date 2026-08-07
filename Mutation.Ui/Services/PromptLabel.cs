namespace Mutation.Ui.Services;

/// <summary>
/// How a prompt is referred to inside a sentence a screen reader will read out: a quoted name
/// when there is one, a neutral fallback when there is not.
/// <para>
/// Shared so the confirmation dialog, the outcome announcements, and the accessible names on
/// the per-row buttons all name the same prompt the same way. Hearing "Delete prompt
/// 'Summarise'" and then "Delete this prompt?" for the same row is a small thing to read and
/// a confusing thing to hear.
/// </para>
/// </summary>
internal static class PromptLabel
{
	/// <summary>Fallback label when a prompt has no usable name.</summary>
	internal const string Unnamed = "this prompt";

	internal static string Describe(string? name) =>
		string.IsNullOrWhiteSpace(name)
			? Unnamed
			: $"prompt '{name.Trim()}'";
}
