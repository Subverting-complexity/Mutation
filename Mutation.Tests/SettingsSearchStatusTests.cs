using Mutation.Ui.Views.SettingsUi;

namespace Mutation.Tests;

public class SettingsSearchStatusTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Compose_EmptyQuery_ReturnsEmpty(string? query)
	{
		Assert.Equal(string.Empty, SettingsSearchStatus.Compose(query, 9, 9, 5));
	}

	[Fact]
	public void Compose_NoCategoryMatches_SaysNoMatch()
	{
		Assert.Equal("No categories match.", SettingsSearchStatus.Compose("zzz", 0, 9, null));
	}

	[Fact]
	public void Compose_NoPageLoaded_ReportsCategoriesOnly()
	{
		Assert.Equal("3 of 9 categories match.", SettingsSearchStatus.Compose("audio", 3, 9, null));
	}

	[Fact]
	public void Compose_WithPageMatches_ReportsBothCounts()
	{
		Assert.Equal("3 of 9 categories match. 2 matching sections shown on this page.",
			SettingsSearchStatus.Compose("audio", 3, 9, 2));
	}

	[Fact]
	public void Compose_SingleSectionMatch_UsesSingular()
	{
		Assert.Equal("1 of 9 categories match. 1 matching section shown on this page.",
			SettingsSearchStatus.Compose("mute", 1, 9, 1));
	}

	[Fact]
	public void Compose_ZeroSectionsOnMatchingPage_ReportsZero()
	{
		Assert.Equal("2 of 9 categories match. 0 matching sections shown on this page.",
			SettingsSearchStatus.Compose("key", 2, 9, 0));
	}
}
