using System;
using System.Threading;

namespace Mutation.Ui.Core;

/// <summary>
/// The one in-flight language-model call, and the handle that cuts it short. A call can
/// run for minutes — the retry ladder escalates its timeout on every attempt, and a Fast
/// mode request that falls back to standard speed climbs the whole thing twice — so the
/// user needs a way out of it and a closing window needs the call to stop (issue #256).
/// </summary>
/// <remarks>
/// <para>
/// Cancelling never disposes the source. <see cref="Cancel"/> can run while the owning
/// call is still between retries, and each retry links a fresh token to this source, so
/// disposing there would surface ObjectDisposedException in place of the clean
/// cancellation the caller is waiting to catch. Disposal waits for the run's own
/// <see cref="Run.Dispose"/>, the one place that knows every attempt has finished.
/// </para>
/// <para>
/// Ownership is the reason <see cref="Begin"/> hands back a <see cref="Run"/> rather than
/// leaving the caller to call an <c>End()</c>. Two runs can overlap — a caller that checks
/// <see cref="Running"/> before an await is not holding the slot yet — and a plain
/// <c>End()</c> released whatever was current, so the first run to unwind cancelled and
/// disposed the *second* run's source. Both calls died from a single double-start. A run
/// now releases the slot only while it still owns it.
/// </para>
/// </remarks>
internal sealed class LlmOperationState : IDisposable
{
	private readonly object _ctsLock = new();
	private CancellationTokenSource? _cts;

	// Identifies which run owns the slot. Monotonic, so a superseded run can never match.
	private long _generation;

	/// <summary>
	/// Whether a call is in flight. What the UI reads to decide that a press of the
	/// control that started the call means "stop" rather than "start another".
	/// </summary>
	internal bool Running
	{
		get { lock (_ctsLock) return _cts is not null; }
	}

	/// <summary>
	/// Whether a stop has already been asked for and the call is still winding down.
	/// A repeat press is answered with "already stopping" rather than re-announcing the
	/// request — hearing the same sentence twice is the complaint issue #299 was about,
	/// and an unwinding HTTP read can take long enough to invite a second press.
	/// </summary>
	internal bool CancelRequested
	{
		get { lock (_ctsLock) return _cts is not null && _cts.IsCancellationRequested; }
	}

	/// <summary>
	/// Claims the slot and returns a handle whose token is tied to both this run and
	/// <paramref name="external"/> (the window's shutdown token at every call site).
	/// Dispose the handle — <c>using</c>, or a finally — once the call and every retry
	/// have finished. The caller MUST pass <see cref="Run.Token"/> down rather than
	/// re-reading state, or it races a <see cref="Cancel"/> from another thread.
	/// </summary>
	internal Run Begin(CancellationToken external = default)
	{
		lock (_ctsLock)
		{
			// A previous run should have released the slot, but if a stale source lingers,
			// cancel it before disposing so anything still tied to it unwinds as a
			// cancellation rather than being abandoned mid-flight.
			if (_cts is not null)
			{
				try { _cts.Cancel(); } catch (ObjectDisposedException) { }
				_cts.Dispose();
			}
			_cts = CancellationTokenSource.CreateLinkedTokenSource(external);
			return new Run(this, ++_generation, _cts.Token);
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

	// Releases the slot only if `generation` still owns it. A superseded run calling this
	// is a no-op: its source was already cancelled and disposed by the Begin that
	// replaced it, and the source now in the field belongs to somebody else.
	//
	// Disposes without cancelling, deliberately. The run has finished by definition, so
	// there is nothing left to signal — and cancelling here would leave the token reading
	// as cancelled after an ordinary success or a timeout, which is exactly the question
	// the callers ask it ("did somebody cancel this, or did the provider time out?") to
	// decide what to tell the user.
	private void Release(long generation)
	{
		CancellationTokenSource? toDispose;
		lock (_ctsLock)
		{
			if (_generation != generation)
				return;
			toDispose = _cts;
			_cts = null;
		}
		toDispose?.Dispose();
	}

	/// <summary>
	/// Cancels and releases whatever is running, whoever owns it. For teardown only —
	/// a run releases its own slot through <see cref="Run.Dispose"/>.
	/// </summary>
	public void Dispose()
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

	/// <summary>
	/// One claim on the slot. Disposing it releases the claim, but only while this run is
	/// still the one holding it.
	/// </summary>
	internal readonly struct Run : IDisposable
	{
		private readonly LlmOperationState _owner;
		private readonly long _generation;

		internal Run(LlmOperationState owner, long generation, CancellationToken token)
		{
			_owner = owner;
			_generation = generation;
			Token = token;
		}

		/// <summary>The token to hand down to the language-model call.</summary>
		internal CancellationToken Token { get; }

		public void Dispose() => _owner?.Release(_generation);
	}
}
