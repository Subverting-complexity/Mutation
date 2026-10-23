namespace Mutation.Ui.Core;

/// <summary>
/// The three things <see cref="MainWindowActivationLock"/> needs to know about, or do to, the
/// main window. A seam so the lock's rules can be tested without a real window.
/// </summary>
public interface IMainWindowEnablement
{
	/// <summary>Whether the main window is the foreground window right now.</summary>
	bool IsForeground { get; }

	/// <summary>Whether the main window accepts input right now.</summary>
	bool IsEnabled { get; }

	/// <summary>Enables or disables the main window, the way a modal dialog does to its owner.</summary>
	void SetEnabled(bool enabled);
}
