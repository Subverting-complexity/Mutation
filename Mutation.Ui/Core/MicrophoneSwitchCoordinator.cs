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

	// What a queued request asks the worker to do.
	private enum RequestKind
	{
		// Select the requested device, then point capture at it.
		Switch,

		// Re-open capture on whatever device is already selected, without touching
		// the selection. For recovering capture that a failed switch left with
		// nothing running.
		Restart,

		// Release capture; there is no device to record from.
		Release,
	}

	// The request most recently made but not yet picked up, and the awaiter tied to
	// it. All three are cleared the moment the worker picks the request up. Guarded
	// by _gate.
	private RequestKind _pendingKind;
	private string? _pendingDeviceId;
	private TaskCompletionSource<MicrophoneSwitchResult?>? _pendingCompletion;

	// True while the drain worker is alive (a switch is in flight or a request is
	// queued). Guarded by _gate.
	private bool _workerRunning;

	// Handed to callers of WaitForIdleAsync and completed when the worker goes idle.
	// Created on demand, so a coordinator nobody waits on allocates nothing. Guarded by
	// _gate.
	private TaskCompletionSource<bool>? _idleCompletion;

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
	/// Completes when no request is in flight or queued — i.e. when the selected device
	/// and the capture handle have caught up with what the user last asked for.
	///
	/// <para>
	/// This is what a dictation start waits on. <see cref="SwitchAsync"/>'s own task is
	/// no use there: it belongs to whoever made the request, and a superseded one
	/// completes with <c>null</c> the moment a newer request arrives rather than when
	/// the device is actually open (issue #312).
	/// </para>
	///
	/// <para>
	/// It carries no deadline of its own. A wedged winmm call cannot be cancelled, so a
	/// device that never answers never completes this — the caller must bound its own
	/// wait.
	/// </para>
	/// </summary>
	public Task WaitForIdleAsync()
	{
		lock (_gate)
		{
			if (!_workerRunning)
				return Task.CompletedTask;

			// One source shared by every waiter: they are all waiting for the same thing.
			return (_idleCompletion ??= new TaskCompletionSource<bool>(
				TaskCreationOptions.RunContinuationsAsynchronously)).Task;
		}
	}

	// Called wherever _workerRunning is cleared, always with _gate held, and its result
	// completed outside the lock so no continuation runs under it.
	private TaskCompletionSource<bool>? TakeIdleCompletion()
	{
		var idle = _idleCompletion;
		_idleCompletion = null;
		return idle;
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

		return EnqueueAsync(RequestKind.Switch, deviceId);
	}

	/// <summary>
	/// Requests that capture be re-opened on the device that is already selected,
	/// without changing the selection. A switch that reports
	/// <see cref="MicrophoneSwitchOutcome.Unavailable"/> or
	/// <see cref="MicrophoneSwitchOutcome.Failed"/> never reaches its capture restart,
	/// which at app startup means nothing is capturing at all; this puts the waveform
	/// and the level meter back on the device that is actually live rather than
	/// leaving them dead for the session.
	/// </summary>
	public Task<MicrophoneSwitchResult?> RestartCaptureAsync() => EnqueueAsync(RequestKind.Restart, null);

	/// <summary>
	/// Requests that capture be released, for when the selection has become no device
	/// at all. Queued on the same worker as the switches, so it cannot overtake or be
	/// overtaken by one, and closing the capture handle — a winmm call like any other
	/// — stays off the UI thread.
	/// </summary>
	public Task<MicrophoneSwitchResult?> ReleaseAsync() => EnqueueAsync(RequestKind.Release, null);

	private Task<MicrophoneSwitchResult?> EnqueueAsync(RequestKind kind, string? deviceId)
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
			_pendingKind = kind;
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

	// Nothing should escape DrainLoop — Apply catches every device fault. This is the
	// backstop for the one failure mode that would be permanent: a worker that dies
	// with _workerRunning still set is a coordinator that never starts another one, so
	// every microphone switch for the rest of the session would hang unreported.
	private void Drain()
	{
		try
		{
			DrainLoop();
		}
		catch (Exception ex)
		{
			TaskCompletionSource<MicrophoneSwitchResult?>? orphan;
			TaskCompletionSource<bool>? idle;
			lock (_gate)
			{
				_workerRunning = false;
				orphan = _pendingCompletion;
				_pendingDeviceId = null;
				_pendingCompletion = null;
				idle = TakeIdleCompletion();
			}

			orphan?.TrySetResult(null);
			// Released here too, and not only on the clean exit below: a waiter parked on
			// a worker that died would otherwise never be woken, which is the permanent
			// dead hotkey this whole backstop exists to prevent.
			idle?.TrySetResult(true);
			ReportError(ex);
		}
	}

	// Applies queued requests one at a time until none remain. Only the request
	// currently held in _pendingCompletion is applied each pass, so a burst collapses
	// to its most recent entry.
	private void DrainLoop()
	{
		while (true)
		{
			RequestKind kind;
			string? deviceId;
			TaskCompletionSource<MicrophoneSwitchResult?> completion;

			lock (_gate)
			{
				if (_pendingCompletion is null)
				{
					_workerRunning = false;
					var idle = TakeIdleCompletion();
					// Completed asynchronously (the source is built that way), so this
					// does not run a continuation under _gate.
					idle?.TrySetResult(true);
					return;
				}

				kind = _pendingKind;
				deviceId = _pendingDeviceId;
				completion = _pendingCompletion;
				_pendingDeviceId = null;
				_pendingCompletion = null;
			}

			MicrophoneSwitchResult result = Apply(kind, deviceId);

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

	private MicrophoneSwitchResult Apply(RequestKind kind, string? deviceId)
	{
		try
		{
			switch (kind)
			{
				case RequestKind.Release:
					_stopCapture();
					break;

				case RequestKind.Restart:
					_restartCapture();
					break;

				case RequestKind.Switch:
					// The device may have been unplugged between the click and this
					// call; the selection is left where it was and the caller says so.
					// Never null here — SwitchAsync is the only way to reach this kind
					// and it rejects an empty ID.
					if (!_selectDevice(deviceId!))
						return new MicrophoneSwitchResult(MicrophoneSwitchOutcome.Unavailable);

					_restartCapture();
					break;

				default:
					// A kind added later must land here rather than quietly taking the
					// switch branch and dereferencing a device ID it does not carry.
					throw new NotSupportedException($"Unhandled microphone request kind: {kind}.");
			}

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
