using CognitiveSupport;
using System;
using System.Collections.Generic;

namespace Mutation.Ui.Services;

/// <summary>
/// Every change the prompt library makes to the list of prompts, with none of the window
/// that asks for it.
/// <para>
/// <see cref="PromptLibraryController"/> opens a <c>PromptEditorWindow</c> for each of add
/// and edit and does the list work inside the window's <c>Closed</c> handler, so there was
/// no add or delete to call without a UI and nothing to test but the whole screen. The rules
/// worth testing are all in here: which prompt ends up carrying <c>AutoRun</c>, and whether
/// every prompt still has a distinct Id afterwards (issue #304).
/// </para>
/// <para>
/// The <c>AutoRun</c> sweep in particular was written out twice, once in the add path and
/// once in the edit path, with the two spelling the "every prompt but this one" test
/// differently — the add path cleared the flag on the whole list before appending the new
/// prompt, the edit path skipped the prompt by reference. One rule now serves both.
/// </para>
/// </summary>
internal static class PromptLibraryMutations
{
	/// <summary>
	/// Appends <paramref name="prompt"/>. If it wants to be the auto-run prompt it takes the
	/// flag off every other prompt, and if it does not, whichever prompt already had it keeps
	/// it. Ids are handed out afterwards, so the new prompt gets one and any hand-written
	/// clash in the file is repaired at the same time.
	/// <para>
	/// Ids are assigned from the lowest free number rather than above the highest in use
	/// (see <see cref="PromptIdBackfill"/>), which is what makes add-after-delete safe: the
	/// number the deleted prompt gave up is free, so the new prompt takes it instead of
	/// repeating one that is still in use.
	/// </para>
	/// </summary>
	internal static void Add(IList<LlmSettings.LlmPrompt> prompts, LlmSettings.LlmPrompt prompt)
	{
		if (prompts is null) throw new ArgumentNullException(nameof(prompts));
		if (prompt is null) throw new ArgumentNullException(nameof(prompt));

		prompts.Add(prompt);
		ApplyAutoRunExclusivity(prompts, prompt);
		PromptIdBackfill.Apply(AsReadOnlyList(prompts));
	}

	/// <summary>
	/// Settles the list after <paramref name="prompt"/> was edited in place. The editor window
	/// writes straight into the prompt object, so there is nothing to copy back — the only
	/// thing left to decide is whether it has just claimed <c>AutoRun</c> from someone else.
	/// <para>
	/// No Id is handed out here on purpose. An edit adds no prompt, so it cannot leave one
	/// without an Id, and renumbering a prompt the user was only renaming would move an
	/// identity they did not ask to change.
	/// </para>
	/// </summary>
	internal static void CommitEdit(IList<LlmSettings.LlmPrompt> prompts, LlmSettings.LlmPrompt prompt)
	{
		if (prompts is null) throw new ArgumentNullException(nameof(prompts));
		if (prompt is null) throw new ArgumentNullException(nameof(prompt));

		ApplyAutoRunExclusivity(prompts, prompt);
	}

	/// <summary>
	/// Removes <paramref name="prompt"/>. Returns false when there was nothing to remove — a
	/// null prompt, or one already gone — so the caller can say what actually happened rather
	/// than announcing a deletion that did not occur.
	/// </summary>
	internal static bool Delete(IList<LlmSettings.LlmPrompt> prompts, LlmSettings.LlmPrompt? prompt)
	{
		if (prompts is null) throw new ArgumentNullException(nameof(prompts));

		return prompt is not null && prompts.Remove(prompt);
	}

	/// <summary>The prompt that runs by itself when a transcript arrives, or null when none does.</summary>
	internal static LlmSettings.LlmPrompt? AutoRunPrompt(IEnumerable<LlmSettings.LlmPrompt>? prompts)
	{
		if (prompts is null)
			return null;

		foreach (var prompt in prompts)
		{
			if (prompt.AutoRun)
				return prompt;
		}

		return null;
	}

	/// <summary>
	/// Leaves <paramref name="claimant"/> as the only prompt with <c>AutoRun</c> set, but only
	/// if it has it. A prompt saved without asking for auto-run does not take the flag away
	/// from the prompt that already holds it.
	/// <para>
	/// Compared by reference rather than by Id, because this also runs for a prompt that has
	/// not been given an Id yet.
	/// </para>
	/// </summary>
	private static void ApplyAutoRunExclusivity(
		IList<LlmSettings.LlmPrompt> prompts, LlmSettings.LlmPrompt claimant)
	{
		if (!claimant.AutoRun)
			return;

		foreach (var other in prompts)
		{
			if (!ReferenceEquals(other, claimant))
				other.AutoRun = false;
		}
	}

	/// <summary>
	/// The list as something <see cref="PromptIdBackfill"/> can read. A <c>List&lt;T&gt;</c> —
	/// which is what every caller passes — is already one; anything else is copied, and the
	/// copy is fine because the backfill mutates the prompts rather than the list holding them.
	/// </summary>
	private static IReadOnlyList<LlmSettings.LlmPrompt> AsReadOnlyList(IList<LlmSettings.LlmPrompt> prompts) =>
		prompts as IReadOnlyList<LlmSettings.LlmPrompt> ?? new List<LlmSettings.LlmPrompt>(prompts);
}
