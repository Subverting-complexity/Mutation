using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>
/// Runs a mute toggle off the caller's thread so the COM writes, read-back
/// verification, and any device re-enumeration in the underlying toggle never
/// block the UI thread (and, with it, the screen reader). The toggle itself is
/// injected as a delegate, keeping this coordinator unit-testable without audio
/// hardware or a UI.
///
/// Overlapping presses are coalesced: while one toggle is in flight, a second
/// request is dropped rather than queued, so a rapid double-press cannot stack
/// two flips and leave the mute state where it started.
/// </summary>
public sealed class MicrophoneMuteToggleCoordinator
{
	private readonly Func<MuteToggleResult> _toggle;

	// 0 = idle, 1 = a toggle is running. Flipped atomically so two threads
	// racing on the hotkey cannot both start a toggle.
	private int _inFlight;

	public MicrophoneMuteToggleCoordinator(Func<MuteToggleResult> toggle)
	{
		_toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
	}

	/// <summary>
	/// True while a toggle started by this coordinator has not yet completed.
	/// </summary>
	public bool IsToggling => Volatile.Read(ref _inFlight) != 0;

	/// <summary>
	/// Runs the injected toggle on a background thread and returns its confirmed
	/// result. Returns <c>null</c> when a toggle is already in flight — the
	/// request is coalesced, not queued. The continuation resumes on the caller's
	/// synchronization context (the UI thread when called from a UI event), so
	/// the caller can update the UI directly with the result.
	/// </summary>
	public async Task<MuteToggleResult?> ToggleAsync()
	{
		// Claim the single in-flight slot; bail out if another toggle holds it.
		if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
			return null;

		try
		{
			return await Task.Run(_toggle);
		}
		finally
		{
			Volatile.Write(ref _inFlight, 0);
		}
	}
}
