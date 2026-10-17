using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// What the prompt editor's reasoning level list shows for a model. The list must show the
/// level a run would actually send, so these cases follow the resolver's rules.
/// </summary>
public class ReasoningLevelPickTests
{
	private static LlmModelConfig Model(LlmProvider provider, params ReasoningLevel[] ticked) =>
		new("m", provider) { ReasoningLevels = ticked.ToList() };

	[Fact]
	public void For_ShowsOnlyTheTickedLevels_LightestFirst()
	{
		var model = Model(LlmProvider.Anthropic, ReasoningLevel.High, ReasoningLevel.Off, ReasoningLevel.Low);

		var pick = ReasoningLevelPick.For(model, ReasoningLevel.High);

		Assert.Equal(new[] { ReasoningLevel.Off, ReasoningLevel.Low, ReasoningLevel.High }, pick.Levels);
		Assert.True(pick.Reasons);
	}

	[Fact]
	public void For_SelectsTheWantedLevel_WhenItIsTicked()
	{
		var model = Model(LlmProvider.OpenAI, ReasoningLevel.None, ReasoningLevel.Low, ReasoningLevel.High);

		var pick = ReasoningLevelPick.For(model, ReasoningLevel.High);

		Assert.Equal(ReasoningLevel.High, pick.Selected);
		Assert.Equal(2, pick.SelectedIndex);
		Assert.False(pick.MovedFrom(ReasoningLevel.High));
	}

	[Fact]
	public void For_MovesToTheLightestTickedLevel_NotCountingOffOrNone()
	{
		// The new model does not accept the prompt's level, so the list moves, and the
		// editor must say what changed.
		var model = Model(LlmProvider.Anthropic, ReasoningLevel.Off, ReasoningLevel.Medium, ReasoningLevel.Max);

		var pick = ReasoningLevelPick.For(model, ReasoningLevel.High);

		Assert.Equal(ReasoningLevel.Medium, pick.Selected);
		Assert.Equal(1, pick.SelectedIndex);
		Assert.True(pick.MovedFrom(ReasoningLevel.High));
	}

	[Fact]
	public void For_ShowsOffOrNone_WhenTheyAreTheOnlyLevelsTicked()
	{
		// A run sends no level here, which is what Off means, so the list shows Off
		// rather than a blank selection.
		var model = Model(LlmProvider.Anthropic, ReasoningLevel.Off);

		var pick = ReasoningLevelPick.For(model, ReasoningLevel.Low);

		Assert.Equal(ReasoningLevel.Off, pick.Selected);
		Assert.True(pick.MovedFrom(ReasoningLevel.Low));
	}

	[Fact]
	public void For_TurnsTheListOff_WhenNothingIsTicked()
	{
		var pick = ReasoningLevelPick.For(Model(LlmProvider.OpenAI), ReasoningLevel.Low);

		Assert.False(pick.Reasons);
		Assert.Empty(pick.Levels);
		Assert.Null(pick.Selected);
		Assert.Equal(-1, pick.SelectedIndex);
		// Nothing moved: the prompt keeps its level for a model that reasons.
		Assert.False(pick.MovedFrom(ReasoningLevel.Low));
	}

	[Fact]
	public void For_TurnsTheListOff_ForAModelThatIsNotInTheModelsList()
	{
		var pick = ReasoningLevelPick.For(null, ReasoningLevel.Low);

		Assert.False(pick.Reasons);
		Assert.Null(pick.Selected);
	}

	[Fact]
	public void For_LeavesOutLevelsFromTheOtherService()
	{
		// Max is Anthropic only; an OpenAI model with it ticked (a hand-edited file) must
		// not offer it, because the service would refuse it.
		var model = Model(LlmProvider.OpenAI, ReasoningLevel.Max, ReasoningLevel.Minimal);

		var pick = ReasoningLevelPick.For(model, ReasoningLevel.Max);

		Assert.Equal(new[] { ReasoningLevel.Minimal }, pick.Levels);
		Assert.Equal(ReasoningLevel.Minimal, pick.Selected);
	}

	[Fact]
	public void For_SelectsTheLevelTheResolverSends()
	{
		// The editor and a run must agree, whatever is ticked.
		var model = Model(LlmProvider.Anthropic, ReasoningLevel.Off, ReasoningLevel.ExtraHigh);

		foreach (var wanted in Enum.GetValues<ReasoningLevel>())
		{
			var pick = ReasoningLevelPick.For(model, wanted);
			var sent = ReasoningLevelResolver.Resolve(model, wanted);
			Assert.Equal(sent ?? ReasoningLevel.Off, pick.Selected);
		}
	}
}
