using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class StatusAnnouncementTests
{
	[Fact]
	public void ComposeText_JoinsTitleAndMessage()
	{
		Assert.Equal("Speech to Text: Transcript ready.",
			StatusAnnouncement.ComposeText("Speech to Text", "Transcript ready."));
	}

	[Fact]
	public void ComposeText_TitleOnly_ReturnsTitle()
	{
		Assert.Equal("Microphone", StatusAnnouncement.ComposeText("Microphone", "  "));
	}

	[Fact]
	public void ComposeText_MessageOnly_ReturnsMessage()
	{
		Assert.Equal("Transcript ready.", StatusAnnouncement.ComposeText(null, "Transcript ready."));
	}

	[Fact]
	public void ComposeText_BothEmpty_ReturnsEmpty()
	{
		Assert.Equal(string.Empty, StatusAnnouncement.ComposeText(null, ""));
	}

	[Theory]
	[InlineData(InfoBarSeverity.Error, AutomationNotificationKind.ActionAborted)]
	[InlineData(InfoBarSeverity.Success, AutomationNotificationKind.ActionCompleted)]
	[InlineData(InfoBarSeverity.Warning, AutomationNotificationKind.Other)]
	[InlineData(InfoBarSeverity.Informational, AutomationNotificationKind.Other)]
	public void GetKind_MapsSeverity(InfoBarSeverity severity, AutomationNotificationKind expected)
	{
		Assert.Equal(expected, StatusAnnouncement.GetKind(severity));
	}

	[Theory]
	[InlineData(InfoBarSeverity.Error, AutomationNotificationProcessing.ImportantMostRecent)]
	[InlineData(InfoBarSeverity.Warning, AutomationNotificationProcessing.ImportantMostRecent)]
	[InlineData(InfoBarSeverity.Success, AutomationNotificationProcessing.MostRecent)]
	[InlineData(InfoBarSeverity.Informational, AutomationNotificationProcessing.MostRecent)]
	public void GetProcessing_ErrorsAndWarningsInterrupt(InfoBarSeverity severity, AutomationNotificationProcessing expected)
	{
		Assert.Equal(expected, StatusAnnouncement.GetProcessing(severity));
	}
}
