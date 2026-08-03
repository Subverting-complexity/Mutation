using CognitiveSupport;
using System.Collections.Generic;

namespace Mutation.Ui.Services;

/// <summary>
/// Gives every LLM prompt a distinct positive <c>Id</c>.
///
/// Ids are only ever assigned when a prompt is added through the UI, so a prompt written
/// straight into Mutation.json by hand — a shape the README documents — leaves the field
/// at the <c>int</c> default of 0. Several such prompts then look like one prompt to
/// anything that identifies a prompt by Id, and the app quietly attributes one prompt's
/// state to all of them.
///
/// Pure and list-based so the assignment rule can be tested without touching the disk.
/// </summary>
internal static class PromptIdBackfill
{
	/// <summary>
	/// Assigns an Id to every prompt that has none (0 or negative) or that repeats an Id
	/// an earlier prompt already claimed. Prompts whose Id is already valid and unique
	/// keep it, so a user's hand-chosen numbering survives and Ids stay stable across
	/// loads. Returns true when anything was assigned, so the caller knows to persist.
	/// </summary>
	internal static bool Apply(IReadOnlyList<LlmSettings.LlmPrompt>? prompts)
	{
		if (prompts is null || prompts.Count == 0)
			return false;

		// Every Id already spoken for, gathered up front so a prompt later in the list
		// does not have its Id handed to an earlier one that was missing one.
		var reserved = new HashSet<int>();
		foreach (var prompt in prompts)
		{
			if (prompt is not null && prompt.Id > 0)
				reserved.Add(prompt.Id);
		}

		var taken = new HashSet<int>();
		bool changed = false;
		int candidate = 1;

		foreach (var prompt in prompts)
		{
			if (prompt is null)
				continue;

			// First prompt to claim a given valid Id keeps it; a repeat is treated as
			// unidentified and renumbered.
			if (prompt.Id > 0 && taken.Add(prompt.Id))
				continue;

			while (reserved.Contains(candidate) || taken.Contains(candidate))
				candidate++;

			taken.Add(candidate);
			prompt.Id = candidate;
			changed = true;
		}

		return changed;
	}
}
