#nullable enable
namespace Mutation.Ui.Core;

/// <summary>
/// A COM-free description of a capture device, captured at the moment it was
/// enumerated.
///
/// The UI binds these rather than live <c>MMDevice</c> wrappers. The device list is
/// re-enumerated on every hot-plug and on the mute and level retry paths, and the
/// superseded wrappers are disposed — so a list of live devices held by the UI goes
/// stale, and every read of it (including a display binding) then drives a dead COM
/// proxy (issue #264). An immutable snapshot cannot go stale in that way: at worst it
/// names a device that no longer exists, which the selection path detects by ID.
/// </summary>
/// <param name="Id">The CoreAudio endpoint ID — stable across re-enumerations, and
/// what the selection path re-resolves a live device from.</param>
/// <param name="FriendlyName">The name to show the user.</param>
public sealed record CaptureDeviceInfo(string Id, string FriendlyName)
{
	public override string ToString() => FriendlyName;
}
