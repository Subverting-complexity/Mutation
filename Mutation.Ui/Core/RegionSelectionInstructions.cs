namespace Mutation.Ui.Core;

/// <summary>
/// What the region overlay says about itself: its name, read as focus lands on it, and the
/// key instructions, read only when the user asks for them with F1 (issue #447).
/// <para>
/// The instructions used to be the overlay's <c>AutomationProperties.HelpText</c>, so a
/// screen reader read the whole sentence on every capture. A user who already knows the
/// keys heard it every time, and it held up whatever they pressed next. The name is all
/// that is said unprompted now; the keys are one press away.
/// </para>
/// </summary>
internal static class RegionSelectionInstructions
{
	/// <summary>
	/// The overlay's accessible name. The XAML sets the same text as the Canvas's
	/// <c>AutomationProperties.Name</c>; this copy is what is announced when focus could not
	/// land on the Canvas and so nothing would read the name out.
	/// </summary>
	public const string OverlayName = "Select screen region";

	/// <summary>Read out when the user presses F1 in the overlay.</summary>
	public const string Keys =
		"Move with the arrow keys. Hold Control for fine steps or Shift for large steps. "
		+ "Home and End jump to the left and right edges, Page Up and Page Down to the top and bottom. "
		+ "Press Enter to set the first corner, move, then press Enter again to capture. "
		+ "Control plus A selects the whole screen. Backspace clears the corner. Escape cancels. "
		+ "F1 repeats these keys.";
}
