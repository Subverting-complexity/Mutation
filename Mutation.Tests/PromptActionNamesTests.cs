using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// The accessible names on the Run / Edit / Delete buttons in the prompt list. Every row shows
/// the same three words, so the name is the only thing that tells a screen-reader user which
/// prompt the button in front of them belongs to (issue #243).
/// </summary>
public class PromptActionNamesTests
{
	[Theory]
	[InlineData("Run", "Run prompt 'Summarise'")]
	[InlineData("Edit", "Edit prompt 'Summarise'")]
	[InlineData("Delete", "Delete prompt 'Summarise'")]
	public void Build_names_the_prompt_the_button_belongs_to(string verb, string expected)
	{
		Assert.Equal(expected, PromptActionNames.Build(verb, "Summarise"));
	}

	[Fact]
	public void Build_trims_the_name_the_way_the_confirmation_dialog_does()
	{
		Assert.Equal("Delete prompt 'Summarise'", PromptActionNames.Build("Delete", "  Summarise  "));
	}

	// A row can exist before it has been named. It still needs a whole sentence, not a
	// dangling verb.
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Build_falls_back_to_a_neutral_label_for_an_unnamed_prompt(string? name)
	{
		Assert.Equal("Delete this prompt", PromptActionNames.Build("Delete", name));
	}

	[Fact]
	public void Build_without_a_verb_still_names_the_prompt()
	{
		Assert.Equal("prompt 'Summarise'", PromptActionNames.Build(" ", "Summarise"));
	}

	// The button and the dialog it opens have to name the prompt identically — hearing
	// "Delete prompt 'Summarise'" and then a question about something else is disorienting.
	[Fact]
	public void Build_agrees_with_the_deletion_confirmation()
	{
		Assert.Contains(
			PromptActionNames.Build("Delete", "Summarise"),
			"Delete prompt 'Summarise'? This cannot be undone.");
		Assert.Equal(
			"Delete prompt 'Summarise'? This cannot be undone.",
			PromptDeletionMessages.BuildConfirmation("Summarise"));
	}
}
