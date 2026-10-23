namespace Mutation.Ui.Core;

/// <summary>The control the main window puts keyboard focus on when Mutation starts.</summary>
public enum StartupFocusTarget
{
	/// <summary>Neither button can take focus, so focus is left where WinUI put it.</summary>
	None,

	/// <summary>The Play button ("Play selected session").</summary>
	Play,

	/// <summary>The Record button.</summary>
	Record,
}

/// <summary>
/// Chooses where keyboard focus starts in the main window (issue #448).
///
/// Left to itself, WinUI put the first focus on the Playback speed dropdown, so a screen
/// reader read that control every time the window was announced. Playing the last
/// recording is what a user most often wants first, so focus starts on Play. When there is
/// nothing to play the Play button is turned off, and a disabled control cannot take focus,
/// so focus goes to Record instead: the next thing a user with no recordings does.
/// </summary>
public static class StartupFocusPlanner
{
	public static StartupFocusTarget Choose(bool playEnabled, bool recordEnabled)
	{
		if (playEnabled)
			return StartupFocusTarget.Play;

		if (recordEnabled)
			return StartupFocusTarget.Record;

		return StartupFocusTarget.None;
	}
}
