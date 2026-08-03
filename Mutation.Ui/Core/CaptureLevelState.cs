#nullable enable
namespace Mutation.Ui.Core;

/// <summary>
/// What a single probe of the active capture device found: whether it exposes a
/// controllable software level, and what that level currently is.
///
/// The two travel together because the UI needs both to set up its level controls, and
/// each is a separate COM round trip to a device that may be slow or failing. Asking
/// for them one at a time doubles the exposure and can straddle a device change, so a
/// disabled control ends up paired with the previous device's level.
/// </summary>
/// <param name="IsSupported">False on a hardware-fixed device, or one whose
/// endpoint-volume object cannot be obtained at all.</param>
/// <param name="Level">The current level as 0–100, or <c>null</c> when it cannot be
/// read — an unsupported device or a transient failure. Deliberately null rather than
/// a default, so a caller can leave its display unchanged instead of showing a
/// misleading value.</param>
public readonly record struct CaptureLevelState(bool IsSupported, int? Level);
