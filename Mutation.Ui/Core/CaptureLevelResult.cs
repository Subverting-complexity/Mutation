namespace Mutation.Ui.Core;

/// <summary>
/// What happened when the pin service tried to apply a capture level.
/// </summary>
public enum CaptureLevelOutcome
{
	/// <summary>The device is hardware-fixed (no controllable level). Not a failure — nothing to signal.</summary>
	Unsupported,

	/// <summary>Nothing to do: the level was already within epsilon of the target, or pinning was disabled.</summary>
	Unchanged,

	/// <summary>The write was issued and confirmed by read-back within epsilon.</summary>
	Applied,

	/// <summary>The write threw or the read-back did not match, even after retrying on fresh references.</summary>
	Failed,
}

/// <summary>
/// Outcome of a capture-level write. <see cref="Failed"/> is the one thing
/// callers act on: it means the hardware level is not the requested value and
/// the user must be told (failure beep + status), so the pin is never silently
/// skipped. Every other outcome is a success the user need not be warned about.
/// </summary>
public readonly record struct CaptureLevelResult(CaptureLevelOutcome Outcome)
{
	/// <summary>True only when the level could not be applied and confirmed.</summary>
	public bool Failed => Outcome == CaptureLevelOutcome.Failed;
}
