using System;

namespace Mutation.Ui.Core;

/// <summary>
/// Notifies when the set of audio capture devices may have changed — a
/// microphone plugged in, removed, enabled, or disabled at the OS level. It
/// exists so <see cref="Mutation.Ui.AudioDeviceManager"/> can re-enumerate its
/// devices in response to a hot-plug instead of only enumerating once at
/// startup, without taking a direct dependency on the CoreAudio COM API.
/// </summary>
public interface ICaptureDeviceChangeNotifier
{
	/// <summary>
	/// Raised when the audio device set changes. Handlers should re-enumerate
	/// capture devices; the event carries no payload because the correct
	/// response is always a full re-enumeration.
	/// </summary>
	event EventHandler? CaptureDevicesChanged;
}
