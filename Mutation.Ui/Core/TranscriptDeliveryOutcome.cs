namespace Mutation.Ui.Core;

/// <summary>
/// How far text got on its way into the application the user was working in.
/// </summary>
public enum TranscriptDeliveryOutcome
{
	/// <summary>
	/// Everything the chosen insert option asked for was carried out — including the
	/// case where nothing had to be inserted at all.
	/// </summary>
	Delivered,

	/// <summary>The clipboard could not be written, so there was nothing to paste.</summary>
	ClipboardBlocked,

	/// <summary>
	/// The keystrokes were submitted but Windows did not accept them all. The usual
	/// cause is a foreground application running with higher privileges than Mutation,
	/// which makes Windows drop injected input silently (issue #232).
	/// </summary>
	InjectionFailed,
}
