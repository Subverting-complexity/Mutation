using System;

namespace Mutation.Ui.Core;

/// <summary>
/// Holds at most one running timer. <see cref="Restart"/> tears the previous timer
/// down before creating the next, so a caller that re-initialises repeatedly cannot
/// leave a trail of live timers ticking behind it.
/// </summary>
/// <remarks>
/// Written for the waveform monitor, whose <c>Initialize</c> runs again on every
/// Settings-dialog save. It used to overwrite its timer field without stopping the
/// old timer, so ten saves left ten 30 FPS timers dispatching onto the UI thread
/// forever, and switching the visualization off stopped only the newest one
/// (issue #231).
///
/// The timer type is a parameter, and creating, starting, and stopping are all
/// injected, so the one-at-a-time rule can be tested without a
/// <c>DispatcherQueue</c> or a live visual tree.
/// </remarks>
/// <typeparam name="TTimer">The timer being held.</typeparam>
internal sealed class SingleTimerSlot<TTimer> : IDisposable
	where TTimer : class
{
	private readonly Func<TTimer> _create;
	private readonly Action<TTimer> _start;
	private readonly Action<TTimer> _stop;
	private TTimer? _current;

	/// <param name="create">Makes a new timer, already configured (interval, handlers).</param>
	/// <param name="start">Starts the timer <paramref name="create"/> returned.</param>
	/// <param name="stop">Stops a timer and unwires whatever <paramref name="create"/>
	/// wired up. Must leave the timer inert — it is dropped straight after.</param>
	public SingleTimerSlot(
		Func<TTimer> create,
		Action<TTimer> start,
		Action<TTimer> stop)
	{
		_create = create ?? throw new ArgumentNullException(nameof(create));
		_start = start ?? throw new ArgumentNullException(nameof(start));
		_stop = stop ?? throw new ArgumentNullException(nameof(stop));
	}

	/// <summary>The running timer, or null when nothing is running.</summary>
	public TTimer? Current => _current;

	/// <summary>True while a timer is running.</summary>
	public bool IsRunning => _current is not null;

	/// <summary>
	/// Stops whatever was running and starts a fresh timer in its place. Safe to
	/// call any number of times; exactly one timer is left running afterwards.
	/// </summary>
	public void Restart()
	{
		Stop();

		TTimer timer = _create();
		// Held before Start so a throwing Start still leaves the timer owned here,
		// and the next Restart (or Dispose) can stop it rather than orphaning it.
		_current = timer;
		_start(timer);
	}

	/// <summary>
	/// Stops the running timer, if there is one. A no-op otherwise.
	/// </summary>
	public void Stop()
	{
		TTimer? timer = _current;
		if (timer is null)
			return;

		// Cleared first so a throwing stop cannot leave the slot pointing at a timer
		// it has already tried to tear down.
		_current = null;
		_stop(timer);
	}

	public void Dispose() => Stop();
}
