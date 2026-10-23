namespace Mutation.Ui.Core;

/// <summary>What a window is, from a capture's point of view.</summary>
public enum CaptureWindowRole
{
	/// <summary>No window at all, as in the gap while the foreground moves.</summary>
	None,

	/// <summary>The capture overlay.</summary>
	Overlay,

	/// <summary>Mutation's main window.</summary>
	MainWindow,

	/// <summary>Any other window, Mutation's own dialogs included.</summary>
	Other,
}
