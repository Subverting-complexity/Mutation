namespace Mutation.Tests;

/// <summary>
/// Stands in for the clock the five remote-service callers arm their waits on, and writes
/// down what each one asked for. Two different waits go through it: Polly's retry backoff,
/// and the deadline each attempt gives itself. Reading those back is what lets a test say
/// "the second attempt got longer than the first" without timing a stopwatch — the same
/// trick <c>TransientRetryTests.DelaysGrowLinearlyFromHalfASecond</c> uses, and the reason
/// issue #253 could take the clock-dependent tests out of this suite.
/// </summary>
/// <remarks>
/// <para>
/// The two are told apart by who is asking, not by how long the wait is.
/// <see cref="CancellationTokenSource"/> passes itself as the timer state when it arms a
/// deadline; Polly's backoff, which goes through <c>Task.Delay</c>, does not. Length would
/// have been the obvious discriminator and is the wrong one: <see cref="OcrService"/>
/// clamps its timeout to as little as one second, which no threshold can tell from a
/// one-second backoff. <c>RecordingClockTests</c> pins the discriminator in both
/// directions, so if a future runtime stops passing the source as state, that fails rather
/// than this quietly filing deadlines as backoffs.
/// </para>
/// <para>
/// A backoff fires at once, so no test sits through one. A deadline is left to a real
/// timer, so it is never sprung on an attempt that is meant to finish.
/// </para>
/// </remarks>
internal sealed class RecordingClock : TimeProvider
{
	private readonly List<TimeSpan> _backoffs = [];
	private readonly List<TimeSpan> _deadlines = [];

	/// <summary>The retry backoff Polly asked for, in order.</summary>
	internal IReadOnlyList<TimeSpan> Backoffs
	{
		get { lock (_backoffs) return _backoffs.ToArray(); }
	}

	/// <summary>The per-attempt deadlines the service armed, in attempt order.</summary>
	internal IReadOnlyList<TimeSpan> Deadlines
	{
		get { lock (_deadlines) return _deadlines.ToArray(); }
	}

	internal IReadOnlyList<double> DeadlineSeconds =>
		Deadlines.Select(d => d.TotalSeconds).ToArray();

	internal IReadOnlyList<double> BackoffMilliseconds =>
		Backoffs.Select(b => b.TotalMilliseconds).ToArray();

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		// A negative due time is Timeout.InfiniteTimeSpan — a timer armed never to fire.
		// Nobody is waiting on it, and collapsing it would fire it immediately.
		if (dueTime < TimeSpan.Zero)
			return base.CreateTimer(callback, state, dueTime, period);

		if (state is CancellationTokenSource)
		{
			lock (_deadlines) _deadlines.Add(dueTime);
			return base.CreateTimer(callback, state, dueTime, period);
		}

		lock (_backoffs) _backoffs.Add(dueTime);
		return base.CreateTimer(callback, state, TimeSpan.Zero, period);
	}
}
