namespace Mutation.Ui.Core;

/// <summary>
/// Abstraction over the active capture device's endpoint level — the Windows
/// 0–100 "Line" slider on the device's Levels tab, exposed by CoreAudio as a
/// 0.0–1.0 scalar (<c>MasterVolumeLevelScalar</c>). Hiding the hardware behind
/// this interface lets the pin logic in <see cref="MicrophoneLevelPinService"/>
/// be unit-tested without audio hardware.
/// </summary>
public interface ICaptureLevelController
{
	/// <summary>
	/// True when the active device exposes a controllable software level. False
	/// for hardware-fixed devices (no endpoint volume object or unreadable level).
	/// </summary>
	bool IsLevelControlSupported { get; }

	/// <summary>
	/// The active device's current level as a 0.0–1.0 scalar, or <c>null</c> when
	/// no device is selected or the level cannot be read.
	/// </summary>
	float? GetLevelScalar();

	/// <summary>
	/// Sets the active device's level (clamped to 0.0–1.0). Never alters mute
	/// state. A no-op when no device is selected or the device is hardware-fixed.
	/// </summary>
	void SetLevelScalar(float scalar);
}
