using System;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>
/// Runs a microphone switch — resolving the chosen device and pointing waveform
/// capture at it — on a single background worker, so neither half ever runs on the
/// UI thread.
///
/// <para>
/// Both halves block on the audio driver stack. Resolving the device walks the
/// winmm device table (<c>waveInGetNumDevs</c> / <c>waveInGetDevCaps</c> for every
/// device), and restarting capture is <c>waveInStop</c>/<c>waveInReset</c>/
/// <c>waveInClose</c> followed by <c>waveInOpen</c>/<c>waveInStart</c>. On a USB
/// device mid-reconnect, a Bluetooth headset, or a device whose driver is wedged,
/// either can take seconds — and on the UI thread that is the window and the screen
/// reader frozen with it (issue #267).
/// </para>
///
/// <para>
/// Requests coalesce to the latest, and the latest also wins the reporting. A
/// request still queued when a newer one arrives is dropped, and a request that was
/// already running when a newer one arrived reports <c>null</c> rather than its own
/// outcome — the newer switch owns the settings, the capture, and the level controls
/// by then, so acting on the older result would leave the UI describing a device the
/// user has moved on from.
/// </para>
///
/// <para>
/// A wedged device still blocks this worker; nothing can cancel a winmm call already
/// inside the driver. What it cannot do any more is block the window. The user keeps
/// their focus and their selection in the microphone combo throughout, and hears the
/// outcome when it arrives.
/// </para>
///
/// The device work is injected as delegates, keeping this coordinator unit-testable
/// without audio hardware or a UI.
/// </summary>
public sealed class MicrophoneSwitchCoordinator
{
	private readonly Func<string, bool> _selectDevice;
	private readonly Action _restartCapture;
	private readonly Action _stopCapture;
	private readonly Action<Exception>? _onError;
	private readonly object _gate = new();

	// The request most recently made but not yet picked up, and the awaiter tied to
	// it. A null ID means "no device — release capture". Both are cleared the moment
	// the worker picks the request up. Guarded by _gate.
	private string? _pendingDeviceId;
	private TaskCompletionSource<MicrophoneSwitchResult?>? _pendingCompletion;

	// True while the drain worker is alive (a switch is in flight or a request is
	// queued). Guarded by _gate.
	private bool _workerRunning;

	/// <param name="selectDevice">Selects the device with the given endpoint ID,
	/// returning false when no such device exists any more.</param>
	/// <param name="restartCapture">Points waveform capture at the newly-selected
	/// device.</param>
	/// <param name="stopCapture">Releases waveform capture, for when there is no
	/// device to switch to.</param>
	/// <param name="onError">Called with the fault behind a
	/// <see cref="MicrophoneSwitchOutcome.Failed"/> result, so it can be logged.
	/// Without it the exception is only ever seen as the message in the result.</param>
	public MicrophoneSwitchCoordinator(
		Func<string, bool> selectDevice,
		Action restartCapture,
		Action stopCapture,
		Action<Exception>? onError = null)
	{
		_selectDevice = selectDevice ?? throw new ArgumentNullException(nameof(selectDevice));
		_restartCapture = restartCapture ?? throw new ArgumentNullException(nameof(restartCapture));
		_stopCapture = stopCapture ?? throw new ArgumentNullException(nameof(stopCapture));
		_onError = onError;
	}

	/// <summary>
	/// True while a switch started by this coordinator is in flight or a newer
	/// request is queued behind it.
	/// </summary>
	public bool IsSwitching
	{
		get { lock (_gate) return _workerRunning; }
	}

	/// <summary>
	/// Requests a switch to the device with the given endpoint ID. The returned task
	/// completes with the outcome when this request is the one the user ends up on,
	/// or with <c>null</c> when a newer request superseded it. The continuation
	/// resumes on the caller's synchronization context (the UI thread when awaited
	/// from a UI event), so the caller can report the outcome directly.
	/// </summary>
	public Task<MicrophoneSwitchResult?> SwitchAsync(string deviceId)
	{
		if (string.IsNullOrEmpty(deviceId))
			throw new ArgumentException("A device ID is required.", nameof(deviceId));

		return EnqueueAsync(deviceId);
	}

	/// <summary>
	/// Requests that capture be released, for when the selection has become no device
	/// at all. Queued on the same worker as the switches, so it cannot overtake or be
	/// overtaken by one, and closing the capture handle — a winmm call like any other
	/// — stays off the UI thread.
	/// </summary>
	public Task<MicrophoneSwitchResult?> ReleaseAsync() => EnqueueAsync(null);

	private Task<MicrophoneSwitchResult?> EnqueueAsync(string? deviceId)
	{
		TaskCompletionSource<MicrophoneSwitchResult?>? superseded;
		var mine = new TaskCompletionSource<MicrophoneSwitchResult?>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		bool startWorker = false;

		lock (_gate)
		{
			// Any request still queued (not yet started) is now stale — this newer one
			// replaces it.
			superseded = _pendingCompletion;
			_pendingDeviceId = deviceId;
			_pendingCompletion = mine;

			if (!_workerRunning)
			{
				_workerRunning = true;
				startWorker = true;
			}
		}

		// A dropped request completes with null. Done outside the lock so a
		// continuation cannot run while it is held.
		superseded?.TrySetResult(null);

		if (startWorker)
			_ = Task.Run(Drain);

		return mine.Task;
	}

	// Applies queued requests one at a time until none remain. Only the request
	// currently held in _pendingCompletion is applied each pass, so a burst collapses
	// to its most recent entry.
	private void Drain()
	{
		while (true)
		{
			string? deviceId;
			TaskCompletionSource<MicrophoneSwitchResult?> completion;

			lock (_gate)
			{
				if (_pendingCompletion is null)
				{
					_workerRunning = false;
					return;
				}

				deviceId = _pendingDeviceId;
				completion = _pendingCompletion;
				_pendingDeviceId = null;
				_pendingCompletion = null;
			}

			MicrophoneSwitchResult result = Apply(deviceId);

			// A newer request arrived while this one was running. It is about to
			// re-point the device and will report its own outcome, so this one reports
			// nothing: acting on it would save the wrong device name, re-probe the
			// level controls against a device that is already being replaced, or show a
			// failure for a choice the user has moved on from.
			bool superseded;
			lock (_gate)
			{
				superseded = _pendingCompletion is not null;
			}

			completion.TrySetResult(superseded ? null : result);
		}
	}

	private MicrophoneSwitchResult Apply(string? deviceId)
	{
		try
		{
			if (deviceId is null)
			{
				_stopCapture();
				return new MicrophoneSwitchResult(MicrophoneSwitchOutcome.Switched);
			}

			// The device may have been unplugged between the click and this call; the
			// selection is left where it was and the caller says so.
			if (!_selectDevice(deviceId))
				return new MicrophoneSwitchResult(MicrophoneSwitchOutcome.Unavailable);

			_restartCapture();
			return new MicrophoneSwitchResult(MicrophoneSwitchOutcome.Switched);
		}
		catch (Exception ex)
		{
			ReportError(ex);
			return new MicrophoneSwitchResult(MicrophoneSwitchOutcome.Failed, ex.Message);
		}
	}

	private void ReportError(Exception exception)
	{
		if (_onError is null)
			return;

		try
		{
			_onError(exception);
		}
		catch
		{
			// A failing reporter must not take the worker with it — the switch's own
			// outcome still has to reach the caller.
		}
	}
}
