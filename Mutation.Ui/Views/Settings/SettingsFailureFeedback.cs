using System;

namespace Mutation.Ui.Views.SettingsUi;

// Composes the user-facing text shown (and announced via UIA) when a Settings
// dialog operation fails — saving the settings file or opening it in an
// external editor. Kept free of WinUI types so it is unit-testable.
public static class SettingsFailureFeedback
{
	public const string SaveTitle = "Save failed";
	public const string OpenJsonTitle = "Could not open settings file";
	public const string BackgroundSaveTitle = "Settings not saved";

	public static string ComposeMessage(Exception? exception) =>
		$"{ExtractDetail(exception)} Your changes were not saved. Fix the problem and press Save again, or press Cancel to discard the changes.";

	// For the saves the main window writes on its own, with no Save button behind
	// them — a slider or toggle change that is persisted a moment after the user
	// stops adjusting it. There is nothing to press again, so the advice is to
	// clear the problem and change the setting once more.
	public static string ComposeBackgroundSaveMessage(Exception? exception) =>
		$"{ExtractDetail(exception)} Your change was not saved and will be lost when Mutation closes. Close anything that has the settings file open, then change the setting again.";

	public static string ComposeOpenJsonMessage(Exception? exception) =>
		ExtractDetail(exception);

	// Innermost exception message — the actionable cause (e.g. the IOException
	// under a serialization wrapper), not the generic outer wrapper text.
	private static string ExtractDetail(Exception? exception)
	{
		Exception? innermost = exception;
		while (innermost?.InnerException is not null)
			innermost = innermost.InnerException;

		string detail = innermost?.Message?.Trim() ?? string.Empty;
		return detail.Length == 0 ? "An unknown error occurred." : detail;
	}
}
