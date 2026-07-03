using CoreAudio;
using System;

namespace Mutation.Ui.Core;

/// <summary>
/// CoreAudio-backed <see cref="ICaptureDeviceChangeNotifier"/>. Wraps an
/// <see cref="MMNotificationClient"/> — the CoreAudio object that registers for
/// OS audio-endpoint notifications — over the shared
/// <see cref="MMDeviceEnumerator"/>, and collapses its per-device events into a
/// single <see cref="CaptureDevicesChanged"/> signal.
///
/// Any device add, remove, or state change triggers the signal — render
/// devices included — because the correct response is always a cheap,
/// idempotent re-enumeration of capture devices, and filtering by data-flow
/// here would add COM lookups for no benefit.
///
/// Registered as an application-lifetime singleton, so it never unsubscribes;
/// the retained <see cref="_notificationClient"/> field also keeps the
/// callback-registered client alive for the life of the process.
/// </summary>
public sealed class MMDeviceCaptureDeviceChangeNotifier : ICaptureDeviceChangeNotifier
{
	private readonly MMNotificationClient _notificationClient;

	public MMDeviceCaptureDeviceChangeNotifier(MMDeviceEnumerator enumerator)
	{
		if (enumerator is null)
			throw new ArgumentNullException(nameof(enumerator));

		_notificationClient = new MMNotificationClient(enumerator);

		// Discard-lambda handlers: every one of these events means the same
		// thing to us — "re-enumerate" — so their differing argument types are
		// irrelevant here.
		_notificationClient.DeviceAdded += (_, _) => Raise();
		_notificationClient.DeviceRemoved += (_, _) => Raise();
		_notificationClient.DeviceStateChanged += (_, _) => Raise();
		_notificationClient.DefaultDeviceChanged += (_, _) => Raise();
	}

	/// <inheritdoc />
	public event EventHandler? CaptureDevicesChanged;

	private void Raise() => CaptureDevicesChanged?.Invoke(this, EventArgs.Empty);
}
