using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Core;

/// <summary>
/// Composes the UIA notification raised for every status InfoBar update.
/// WinUI only announces the InfoBar on its closed→open transition, so text
/// changes while the bar is already open must be announced explicitly.
/// </summary>
public static class StatusAnnouncement
{
	public const string ActivityId = "Mutation.Status";

	public static string ComposeText(string? title, string? message)
	{
		bool hasTitle = !string.IsNullOrWhiteSpace(title);
		bool hasMessage = !string.IsNullOrWhiteSpace(message);

		if (hasTitle && hasMessage)
			return $"{title}: {message}";
		if (hasTitle)
			return title!;
		if (hasMessage)
			return message!;
		return string.Empty;
	}

	public static AutomationNotificationKind GetKind(InfoBarSeverity severity) =>
		severity switch
		{
			InfoBarSeverity.Error => AutomationNotificationKind.ActionAborted,
			InfoBarSeverity.Success => AutomationNotificationKind.ActionCompleted,
			_ => AutomationNotificationKind.Other,
		};

	// Errors and warnings interrupt whatever the screen reader is saying;
	// routine updates stay polite. "MostRecent" keeps rapid sequences
	// (Recording… → Transcribing… → done) from queueing stale messages.
	public static AutomationNotificationProcessing GetProcessing(InfoBarSeverity severity) =>
		severity is InfoBarSeverity.Error or InfoBarSeverity.Warning
			? AutomationNotificationProcessing.ImportantMostRecent
			: AutomationNotificationProcessing.MostRecent;
}
