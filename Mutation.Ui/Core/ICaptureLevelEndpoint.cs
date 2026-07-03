namespace Mutation.Ui.Core;

/// <summary>
/// A single capture device's endpoint level — the Windows 0.0–1.0 scalar behind
/// the "Line" slider — that can be written and read back. Reading or writing may
/// throw (for example, a stale CoreAudio COM proxy on a cached device), which
/// callers treat as a failed write and recover from by retrying against a fresh
/// endpoint. This mirrors <see cref="IMuteEndpoint"/>, so mute and level share
/// the same verify-and-retry recovery shape.
/// </summary>
public interface ICaptureLevelEndpoint
{
	/// <summary>
	/// True when the device exposes a controllable software level (it has an
	/// endpoint volume object). False for a hardware-fixed device or when no
	/// device is selected. This is a stable capability check — it never throws
	/// and never reflects a transient stale-proxy failure, so callers can tell a
	/// genuinely uncontrollable device apart from a write that merely failed.
	/// </summary>
	bool IsLevelControlSupported { get; }

	/// <summary>Reads the current level as a 0.0–1.0 scalar. May throw on a stale/disconnected device.</summary>
	float GetLevelScalar();

	/// <summary>Writes the level (0.0–1.0). May throw on a stale/disconnected device.</summary>
	void SetLevelScalar(float scalar);
}
