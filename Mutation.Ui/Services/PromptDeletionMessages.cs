using System;

namespace Mutation.Ui.Services;

/// <summary>
/// Builds the user-facing wording shown when deleting an LLM prompt: the
/// confirmation question and the outcome announcements. Kept UI-free (no
/// dialog, no InfoBar) so the phrasing a screen-reader user hears is
/// centralized, consistent, and unit-testable in isolation.
/// </summary>
internal static class PromptDeletionMessages
{
	/// <summary>Fallback label when a prompt has no usable name.</summary>
	internal const string UnnamedPrompt = "this prompt";

	internal const string ConfirmationTitle = "Delete prompt";

	/// <summary>Question shown in the confirmation dialog before removal.</summary>
	internal static string BuildConfirmation(string? name) =>
		$"Delete {Describe(name)}? This cannot be undone.";

	/// <summary>Announcement after a prompt was actually removed.</summary>
	internal static string BuildDeleted(string? name) =>
		$"Deleted {Describe(name)}.";

	/// <summary>Announcement when the user cancels the deletion.</summary>
	internal static string BuildCancelled(string? name) =>
		$"Deletion of {Describe(name)} cancelled.";

	/// <summary>
	/// Renders a prompt reference for a sentence: a quoted name when one is
	/// present, or a neutral fallback when the name is missing or blank.
	/// </summary>
	private static string Describe(string? name) =>
		string.IsNullOrWhiteSpace(name)
			? UnnamedPrompt
			: $"prompt '{name.Trim()}'";
}
