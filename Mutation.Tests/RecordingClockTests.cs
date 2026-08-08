using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// <see cref="RecordingClock"/> is what four retry-test classes read their assertions off,
/// so the way it tells a per-attempt deadline from a retry backoff has to be pinned rather
/// than assumed. It tells them apart by who is asking — a <see cref="CancellationTokenSource"/>
/// passes itself as the timer state, Polly's backoff does not. If a future runtime stops
/// doing that, these fail loudly instead of every deadline quietly being filed as a backoff.
/// </summary>
public class RecordingClockTests
{
	[Fact]
	public void ADeadlineIsRecordedAsADeadline_AndIsLeftToRun()
	{
		var clock = new RecordingClock();

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);

		Assert.Equal([30d], clock.DeadlineSeconds);
		Assert.Empty(clock.Backoffs);
		// Left to a real timer: a deadline that fired at once would cancel the very
		// attempt the test wants to watch succeed.
		Assert.False(cts.IsCancellationRequested);
	}

	[Fact]
	public async Task ABackoffIsRecordedAsABackoff_AndFiresAtOnce()
	{
		var clock = new RecordingClock();
		var pipeline = TransientRetry.Pipeline(retryCount: 3, TransientRetry.Transient(), clock);

		await Assert.ThrowsAsync<HttpRequestException>(async () =>
			await pipeline.ExecuteWithAttemptAsync<int>((_, __) =>
				throw new HttpRequestException("transient")));

		Assert.Equal([500d, 1000d, 1500d], clock.BackoffMilliseconds);
		Assert.Empty(clock.Deadlines);
	}

	[Fact]
	public void AShortDeadlineIsStillADeadline()
	{
		// The case that rules out telling the two apart by length. OcrService clamps its
		// per-request timeout to as little as one second, which is shorter than two of the
		// three backoffs — so any threshold would file this under the wrong heading, arm it
		// to fire at once, and cancel the attempt before it started.
		var clock = new RecordingClock();

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1), clock);

		Assert.Equal([1d], clock.DeadlineSeconds);
		Assert.Empty(clock.Backoffs);
		Assert.False(cts.IsCancellationRequested);
	}

	[Fact]
	public void ATimerArmedNeverToFireIsNotRecordedAtAll()
	{
		var clock = new RecordingClock();

		using var timer = clock.CreateTimer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

		Assert.Empty(clock.Deadlines);
		Assert.Empty(clock.Backoffs);
	}
}
