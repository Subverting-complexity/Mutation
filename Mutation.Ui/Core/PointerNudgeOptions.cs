namespace Mutation.Ui.Core;

/// <summary>
/// Whether to nudge the mouse pointer when a capture ends, and how.
/// <para>
/// A workaround for one magnifier behaviour, which is why it is off unless asked for. ZoomText
/// follows the keyboard caret as well as the mouse. When the capture overlay disappears and the
/// application underneath gets the keyboard back, a flashing caret in a text box pulls the
/// magnified view away from the pointer — the pointer is in the right place, but the user cannot
/// see it. ZoomText switches back to the mouse the moment the mouse moves, and a stationary
/// pointer gives it no reason to.
/// </para>
/// </summary>
/// <param name="Enabled">Whether to nudge at all.</param>
/// <param name="IntervalMilliseconds">How long between one one-pixel move and the next.</param>
/// <param name="DurationMilliseconds">How long to keep nudging for, in total.</param>
public readonly record struct PointerNudgeOptions(
	bool Enabled,
	int IntervalMilliseconds,
	int DurationMilliseconds)
{
	/// <summary>What every capture gets unless the user has turned the nudge on.</summary>
	public static PointerNudgeOptions Off => new(false, 0, 0);
}
