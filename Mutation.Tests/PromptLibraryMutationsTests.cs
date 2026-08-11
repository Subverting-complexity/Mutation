using CognitiveSupport;
using Mutation.Ui.Services;

namespace Mutation.Tests;

// The list work behind the prompt library, exercised without a PromptEditorWindow. Every
// path here used to run inside that window's Closed handler, which is why none of it had
// coverage (issue #304).
public class PromptLibraryMutationsTests
{
	private static LlmSettings.LlmPrompt Prompt(
		string name, int id = 0, bool autoRun = false, string? hotkey = null) =>
		new() { Name = name, Id = id, AutoRun = autoRun, Hotkey = hotkey };

	// ----- Add -----

	[Fact]
	public void Add_GivesTheNewPromptAnId()
	{
		var prompts = new List<LlmSettings.LlmPrompt> { Prompt("First", id: 1) };
		var added = Prompt("Second");

		PromptLibraryMutations.Add(prompts, added);

		Assert.Equal(2, prompts.Count);
		Assert.True(added.Id > 0);
		Assert.NotEqual(prompts[0].Id, added.Id);
	}

	[Fact]
	public void Add_AfterADelete_ReusesTheFreedIdRatherThanRepeatingALiveOne()
	{
		// The case the issue calls out. Ids are handed out from the lowest free number, so
		// the gap a deleted prompt leaves is filled. Highest-plus-one would be fine here and
		// wrong against a hand-written Id near int.MaxValue, which is why the rule is the way
		// it is — this test pins the behaviour that follows from it.
		var first = Prompt("First", id: 1);
		var second = Prompt("Second", id: 2);
		var third = Prompt("Third", id: 3);
		var prompts = new List<LlmSettings.LlmPrompt> { first, second, third };

		Assert.True(PromptLibraryMutations.Delete(prompts, second));
		var added = Prompt("Fourth");
		PromptLibraryMutations.Add(prompts, added);

		Assert.Equal(2, added.Id);
		Assert.Equal(3, prompts.Select(p => p.Id).Distinct().Count());
	}

	[Fact]
	public void Add_RepairsHandWrittenPromptsThatShareAnId()
	{
		// A prompt typed straight into Mutation.json carries Id 0, and several of them look
		// like one prompt to anything that identifies a prompt by Id.
		var prompts = new List<LlmSettings.LlmPrompt> { Prompt("Hand written A"), Prompt("Hand written B") };

		PromptLibraryMutations.Add(prompts, Prompt("Added"));

		Assert.Equal(3, prompts.Select(p => p.Id).Distinct().Count());
		Assert.All(prompts, p => Assert.True(p.Id > 0));
	}

	[Fact]
	public void Add_AnAutoRunPrompt_TakesTheFlagOffTheOneThatHadIt()
	{
		var incumbent = Prompt("Incumbent", id: 1, autoRun: true);
		var prompts = new List<LlmSettings.LlmPrompt> { incumbent };
		var added = Prompt("Newcomer", autoRun: true);

		PromptLibraryMutations.Add(prompts, added);

		Assert.False(incumbent.AutoRun);
		Assert.True(added.AutoRun);
	}

	[Fact]
	public void Add_APromptThatDoesNotWantAutoRun_LeavesTheIncumbentHoldingIt()
	{
		// Adding an ordinary prompt is not a reason to switch auto-run off.
		var incumbent = Prompt("Incumbent", id: 1, autoRun: true);
		var prompts = new List<LlmSettings.LlmPrompt> { incumbent };
		var added = Prompt("Newcomer");

		PromptLibraryMutations.Add(prompts, added);

		Assert.True(incumbent.AutoRun);
		Assert.False(added.AutoRun);
	}

	[Fact]
	public void Add_RejectsNulls()
	{
		var prompts = new List<LlmSettings.LlmPrompt>();

		Assert.Throws<ArgumentNullException>(() => PromptLibraryMutations.Add(prompts, null!));
		Assert.Throws<ArgumentNullException>(() => PromptLibraryMutations.Add(null!, Prompt("x")));
	}

	// ----- CommitEdit -----

	[Fact]
	public void CommitEdit_APromptThatJustClaimedAutoRun_TakesItOffEveryOther()
	{
		var first = Prompt("First", id: 1, autoRun: true);
		var second = Prompt("Second", id: 2, autoRun: true);
		var third = Prompt("Third", id: 3, autoRun: true);
		var prompts = new List<LlmSettings.LlmPrompt> { first, second, third };

		PromptLibraryMutations.CommitEdit(prompts, second);

		Assert.False(first.AutoRun);
		Assert.True(second.AutoRun);
		Assert.False(third.AutoRun);
	}

	[Fact]
	public void CommitEdit_APromptWithoutAutoRun_ChangesNobodyElse()
	{
		var incumbent = Prompt("Incumbent", id: 1, autoRun: true);
		var edited = Prompt("Edited", id: 2);
		var prompts = new List<LlmSettings.LlmPrompt> { incumbent, edited };

		PromptLibraryMutations.CommitEdit(prompts, edited);

		Assert.True(incumbent.AutoRun);
		Assert.False(edited.AutoRun);
	}

	[Fact]
	public void CommitEdit_LeavesIdsAlone()
	{
		// Renumbering a prompt the user was only renaming would move an identity they did not
		// ask to change, so an edit hands out no Ids — not even to repair a clash.
		var first = Prompt("First", id: 7);
		var second = Prompt("Second", id: 7);
		var prompts = new List<LlmSettings.LlmPrompt> { first, second };

		PromptLibraryMutations.CommitEdit(prompts, second);

		Assert.Equal(7, first.Id);
		Assert.Equal(7, second.Id);
	}

	// ----- Delete -----

	[Fact]
	public void Delete_RemovesThePromptAndSaysSo()
	{
		var doomed = Prompt("Doomed", id: 2);
		var prompts = new List<LlmSettings.LlmPrompt> { Prompt("Kept", id: 1), doomed };

		Assert.True(PromptLibraryMutations.Delete(prompts, doomed));
		Assert.Single(prompts);
		Assert.Equal("Kept", prompts[0].Name);
	}

	[Fact]
	public void Delete_APromptThatIsNotThere_SaysNothingWasRemoved()
	{
		// The caller announces the outcome, so a false has to mean "nothing happened" rather
		// than "probably fine".
		var prompts = new List<LlmSettings.LlmPrompt> { Prompt("Kept", id: 1) };

		Assert.False(PromptLibraryMutations.Delete(prompts, Prompt("Stranger", id: 9)));
		Assert.False(PromptLibraryMutations.Delete(prompts, null));
		Assert.Single(prompts);
	}

	// ----- AutoRunPrompt -----

	[Fact]
	public void AutoRunPrompt_FindsTheFlaggedOne()
	{
		var wanted = Prompt("Wanted", id: 2, autoRun: true);
		var prompts = new List<LlmSettings.LlmPrompt> { Prompt("First", id: 1), wanted };

		Assert.Same(wanted, PromptLibraryMutations.AutoRunPrompt(prompts));
	}

	[Fact]
	public void AutoRunPrompt_WithNoneFlaggedOrNoList_IsNull()
	{
		Assert.Null(PromptLibraryMutations.AutoRunPrompt(new List<LlmSettings.LlmPrompt> { Prompt("First", id: 1) }));
		Assert.Null(PromptLibraryMutations.AutoRunPrompt(null));
	}
}
