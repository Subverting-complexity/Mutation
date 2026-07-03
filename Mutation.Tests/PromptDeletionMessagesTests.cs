using Mutation.Ui.Services;
using Xunit;

namespace Mutation.Tests;

public class PromptDeletionMessagesTests
{
	[Fact]
	public void BuildConfirmation_NamedPrompt_QuotesNameAndWarnsIrreversible()
	{
		string message = PromptDeletionMessages.BuildConfirmation("Summarize");

		Assert.Equal("Delete prompt 'Summarize'? This cannot be undone.", message);
	}

	[Fact]
	public void BuildConfirmation_TrimsSurroundingWhitespaceInName()
	{
		string message = PromptDeletionMessages.BuildConfirmation("  Summarize  ");

		Assert.Equal("Delete prompt 'Summarize'? This cannot be undone.", message);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void BuildConfirmation_BlankName_FallsBackToNeutralLabel(string? name)
	{
		string message = PromptDeletionMessages.BuildConfirmation(name);

		Assert.Equal("Delete this prompt? This cannot be undone.", message);
	}

	[Fact]
	public void BuildDeleted_NamedPrompt_ReportsRemoval()
	{
		Assert.Equal("Deleted prompt 'Summarize'.", PromptDeletionMessages.BuildDeleted("Summarize"));
	}

	[Fact]
	public void BuildDeleted_BlankName_FallsBackToNeutralLabel()
	{
		Assert.Equal("Deleted this prompt.", PromptDeletionMessages.BuildDeleted("  "));
	}

	[Fact]
	public void BuildCancelled_NamedPrompt_ReportsCancellation()
	{
		Assert.Equal("Deletion of prompt 'Summarize' cancelled.", PromptDeletionMessages.BuildCancelled("Summarize"));
	}

	[Fact]
	public void BuildCancelled_BlankName_FallsBackToNeutralLabel()
	{
		Assert.Equal("Deletion of this prompt cancelled.", PromptDeletionMessages.BuildCancelled(null));
	}
}
