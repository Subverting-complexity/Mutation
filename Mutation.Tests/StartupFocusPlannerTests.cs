using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class StartupFocusPlannerTests
{
	[Theory]
	[InlineData(true, true)]
	[InlineData(true, false)]
	public void Play_is_chosen_whenever_there_is_a_recording_to_play(bool playEnabled, bool recordEnabled)
	{
		Assert.Equal(StartupFocusTarget.Play, StartupFocusPlanner.Choose(playEnabled, recordEnabled));
	}

	[Fact]
	public void Record_is_chosen_when_the_Play_button_is_turned_off()
	{
		Assert.Equal(StartupFocusTarget.Record, StartupFocusPlanner.Choose(playEnabled: false, recordEnabled: true));
	}

	[Fact]
	public void Nothing_is_chosen_when_neither_button_can_take_focus()
	{
		Assert.Equal(StartupFocusTarget.None, StartupFocusPlanner.Choose(playEnabled: false, recordEnabled: false));
	}
}
