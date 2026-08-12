namespace Mutation.Ui.Core;

/// <summary>
/// Whether to nudge the mouse pointer at each end of a capture, and how.
/// <para>
/// A workaround for one magnifier behaviour, which is why it is off unless asked for. ZoomText
/// follows whatever has just taken focus, as well as the mouse, and a capture changes focus
/// twice. When the overlay opens and takes the screen, the magnified view swings to the top-left
/// corner. When it disappears and the application underneath gets the keyboard back, a flashing
/// caret in a text box pulls the view away again. Both times the pointer is in the right place
/// and the user cannot see it. ZoomText switches back to the mouse the moment the mouse moves,
/// and a stationary pointer gives it no reason to.
/// </para>
/// </summary>
/// <param name="Enabled">Whether to nudge at all.</param>
/// <param name="IntervalMilliseconds">How long between one one-pixel move and the next.</param>
/// <param name="DurationMilliseconds">How long one nudge lasts. Spent twice per capture, once
/// at each end.</param>
public readonly record struct PointerNudgeOptions(
	bool Enabled,
	int IntervalMilliseconds,
	int DurationMilliseconds)
{
	/// <summary>What every capture gets unless the user has turned the nudge on.</summary>
	public static PointerNudgeOptions Off => new(false, 0, 0);
}
