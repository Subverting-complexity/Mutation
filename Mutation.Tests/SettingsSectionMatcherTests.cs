using Mutation.Ui.Core;

namespace Mutation.Tests;

// The settings search rule on its own, without a Panel to walk. SettingsSearchStatusTests
// covers the sentence the search box reports afterwards; this covers which sections it is
// counting (issue #304).
public class SettingsSectionMatcherTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t")]
	public void IsEmptyQuery_TreatsNothingTypedAndOnlySpacesTheSame(string? query)
	{
		Assert.True(SettingsSectionMatcher.IsEmptyQuery(query));
	}

	[Fact]
	public void IsEmptyQuery_IsFalseForRealText()
	{
		Assert.False(SettingsSectionMatcher.IsEmptyQuery("beep"));
		Assert.False(SettingsSectionMatcher.IsEmptyQuery("  beep  "));
	}

	[Fact]
	public void Matches_EmptyQuery_MatchesEverySection()
	{
		// An empty search box shows the whole page, including a section with no text in it.
		Assert.True(SettingsSectionMatcher.Matches("Audio device", string.Empty));
		Assert.True(SettingsSectionMatcher.Matches(string.Empty, "   "));
		Assert.True(SettingsSectionMatcher.Matches(null, null));
	}

	[Fact]
	public void Matches_FindsTheQueryAnywhereInTheSection()
	{
		const string section = "Speech to Text  Deepgram API key  Retry count ";

		Assert.True(SettingsSectionMatcher.Matches(section, "Deepgram"));
		Assert.True(SettingsSectionMatcher.Matches(section, "retry"));
		Assert.True(SettingsSectionMatcher.Matches(section, "Speech"));
	}

	[Fact]
	public void Matches_IgnoresCaseInBothDirections()
	{
		Assert.True(SettingsSectionMatcher.Matches("Microphone Level", "MICROPHONE"));
		Assert.True(SettingsSectionMatcher.Matches("MICROPHONE LEVEL", "microphone"));
	}

	[Fact]
	public void Matches_IgnoresSpacesAroundTheQuery()
	{
		// The user typed with a trailing space, or pasted a label with one.
		Assert.True(SettingsSectionMatcher.Matches("Hotkey Router", "  router  "));
	}

	[Fact]
	public void Matches_DoesNotIgnoreSpacesInsideTheQuery()
	{
		// "hotkey router" is a phrase; a section that has both words apart does not answer it.
		Assert.True(SettingsSectionMatcher.Matches("Hotkey Router", "hotkey router"));
		Assert.False(SettingsSectionMatcher.Matches("Hotkey  Router", "hotkey router"));
	}

	[Fact]
	public void Matches_SectionWithoutTheQuery_DoesNotMatch()
	{
		Assert.False(SettingsSectionMatcher.Matches("Audio device", "Deepgram"));
	}

	[Fact]
	public void Matches_SectionWithNoTextAtAll_AnswersOnlyAnEmptyQuery()
	{
		// A section made only of icons and spacing gathers no text. It has to fall out of the
		// results rather than match everything.
		Assert.False(SettingsSectionMatcher.Matches(string.Empty, "beep"));
		Assert.False(SettingsSectionMatcher.Matches(null, "beep"));
	}
}
