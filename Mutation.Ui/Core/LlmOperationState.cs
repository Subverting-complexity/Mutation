using System;
using System.Threading;

namespace Mutation.Ui.Core;

/// <summary>
/// The one in-flight language-model call, and the handle that cuts it short. A call can
/// run for minutes — the retry ladder escalates its timeout on every attempt, and a Fast
/// mode request that falls back to standard speed climbs the whole ladder twice — so the
/// user needs a way out of it and a closing window needs the call to stop (issue #256).
/// </summary>
/// <remarks>
/// Deliberately the same Begin / Cancel / End discipline as
/// <see cref="Mutation.Ui.SpeechToTextState"/>, for the same reason: <see cref="Cancel"/> can run
/// while the owning call is still between retries, and each retry links a fresh token to
/// this source, so cancelling must not dispose it or the operation surfaces
/// ObjectDisposedException instead of a clean cancellation. Disposal waits for
/// <see cref="End"/>, which the owning call raises once every attempt has finished.
/// </remarks>
internal sealed class LlmOperationState : IDisposable
{
	private readonly object _ctsLock = new();
	private CancellationTokenSource? _cts;

	/// <summary>
	/// Whether a call is in flight. What the UI reads to decide that a press of the
	/// hotkey that started the call means "stop" rather than "start another".
	/// </summary>
	internal bool Running
	{
		get { lock (_ctsLock) return _cts is not null; }
	}

	/// <summary>
	/// Claims the slot and returns a token tied to both this operation and
	/// <paramref name="external"/> (the window's shutdown token at every call site).
	/// The caller MUST pass the returned token down rather than re-reading state, or it
	/// races a <see cref="Cancel"/> from another thread.
	/// </summary>
	internal CancellationToken Begin(CancellationToken external = default)
	{
		lock (_ctsLock)
		{
			// A previous call should have ended, but if a stale source lingers, cancel it
			// before disposing so anything still tied to it unwinds as a cancellation
			// rather than being abandoned mid-flight.
			if (_cts is not null)
			{
				try { _cts.Cancel(); } catch (ObjectDisposedException) { }
				_cts.Dispose();
			}
			_cts = CancellationTokenSource.CreateLinkedTokenSource(external);
			return _cts.Token;
		}
	}

	/// <summary>
	/// Signals cancellation without disposing the source. Safe to call when nothing is
	/// running — it does nothing — so the UI never has to check first.
	/// </summary>
	internal void Cancel()
	{
		lock (_ctsLock)
		{
			if (_cts is null)
				return;
			try { _cts.Cancel(); } catch (ObjectDisposedException) { }
		}
	}

	/// <summary>
	/// Releases the slot and disposes the source. Raised by the owning call once its
	/// awaited operation, every retry included, has finished. Idempotent.
	/// </summary>
	internal void End()
	{
		CancellationTokenSource? toDispose;
		lock (_ctsLock)
		{
			toDispose = _cts;
			_cts = null;
		}
		if (toDispose is null)
			return;
		try { toDispose.Cancel(); } catch (ObjectDisposedException) { }
		toDispose.Dispose();
	}

	public void Dispose() => End();
}
