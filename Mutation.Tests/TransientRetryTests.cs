using CognitiveSupport;
using Polly;

namespace Mutation.Tests;

// Issue #259: the five remote-service callers moved off Polly 7's Policy/Context API onto
// Polly 8's resilience pipelines. Every one of them scales its per-attempt timeout by the
// attempt number, and three of them beep once more with each retry, so the attempt count
// and the delay sequence are behaviour, not implementation detail. These tests pin both to
// what Polly 7 produced.
public class TransientRetryTests
{
	[Fact]
	public async Task FirstTryRunsAsAttemptOne()
	{
		var pipeline = TransientRetry.Pipeline(retryCount: 3, TransientRetry.Transient());
		var seen = new List<int>();

		await pipeline.ExecuteWithAttemptAsync((attempt, _) =>
		{
			seen.Add(attempt);
			return ValueTask.FromResult(0);
		});

		Assert.Equal([1], seen);
	}

	[Fact]
	public async Task EachRetryAdvancesTheAttemptNumber()
	{
		var clock = new ImmediateClock();
		var pipeline = TransientRetry.Pipeline(retryCount: 2, TransientRetry.Transient(), clock);
		var seen = new List<int>();

		await Assert.ThrowsAsync<HttpRequestException>(async () =>
			await pipeline.ExecuteWithAttemptAsync<int>((attempt, _) =>
			{
				seen.Add(attempt);
				throw new HttpRequestException("transient");
			}));

		// Three runs in all: the first try plus the two retries, each one attempt higher so
		// the caller's timeout keeps stretching.
		Assert.Equal([1, 2, 3], seen);
	}

	[Fact]
	public async Task DelaysGrowLinearlyFromHalfASecond()
	{
		// Polly 7 produced 500 ms, 1 s, 1.5 s here via Backoff.LinearBackoff(500 ms,
		// factor: 1). Reading the delays Polly asks the clock for — rather than timing the
		// wall clock — is what tells linear apart from exponential, which agrees with it
		// for the first two retries and only diverges on the third.
		var clock = new ImmediateClock();
		var pipeline = TransientRetry.Pipeline(retryCount: 3, TransientRetry.Transient(), clock);

		await Assert.ThrowsAsync<HttpRequestException>(async () =>
			await pipeline.ExecuteWithAttemptAsync<int>((_, __) =>
				throw new HttpRequestException("transient")));

		Assert.Equal([500d, 1000d, 1500d], clock.Delays.Select(d => d.TotalMilliseconds));
	}

	[Fact]
	public async Task NoRetriesConfigured_StillRunsTheBodyOnce()
	{
		// LlmService and AnthropicLlmService both allow a retry count of zero, and Polly 8
		// rejects a retry strategy built for no retries — so no strategy is added at all.
		var pipeline = TransientRetry.Pipeline(retryCount: 0, TransientRetry.Transient());
		int runs = 0;

		await Assert.ThrowsAsync<HttpRequestException>(async () =>
			await pipeline.ExecuteWithAttemptAsync<int>((_, __) =>
			{
				runs++;
				throw new HttpRequestException("transient");
			}));

		Assert.Equal(1, runs);
	}

	[Fact]
	public async Task NegativeRetryCountIsTreatedAsNone()
	{
		var pipeline = TransientRetry.Pipeline(retryCount: -5, TransientRetry.Transient());
		int runs = 0;

		await pipeline.ExecuteWithAttemptAsync((_, __) =>
		{
			runs++;
			return ValueTask.FromResult(0);
		});

		Assert.Equal(1, runs);
	}

	[Fact]
	public async Task UnhandledFailureIsNotRetried()
	{
		// A bad API key has to surface at once rather than after every retry has run.
		var pipeline = TransientRetry.Pipeline(retryCount: 3, TransientRetry.Transient());
		int runs = 0;

		await Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await pipeline.ExecuteWithAttemptAsync<int>((_, __) =>
			{
				runs++;
				throw new InvalidOperationException("permanent");
			}));

		Assert.Equal(1, runs);
	}

	[Fact]
	public async Task ACallerCanAddItsOwnRetryableFailure()
	{
		// OcrService and LlmService both append a status-carrying exception from their own
		// SDK, retried only when the status is transient.
		var clock = new ImmediateClock();
		var pipeline = TransientRetry.Pipeline(
			retryCount: 3,
			TransientRetry.Transient().Handle<StatusException>(ex => ex.Status >= 500),
			clock);

		int serverErrorRuns = 0;
		await Assert.ThrowsAsync<StatusException>(async () =>
			await pipeline.ExecuteWithAttemptAsync<int>((_, __) =>
			{
				serverErrorRuns++;
				throw new StatusException(503);
			}));
		Assert.Equal(4, serverErrorRuns);

		int badRequestRuns = 0;
		await Assert.ThrowsAsync<StatusException>(async () =>
			await pipeline.ExecuteWithAttemptAsync<int>((_, __) =>
			{
				badRequestRuns++;
				throw new StatusException(400);
			}));
		Assert.Equal(1, badRequestRuns);
	}

	[Fact]
	public async Task ReusingOnePipelineDoesNotCarryAttemptsBetweenCalls()
	{
		// The pipelines really are reused: three services hold one in a static field, and
		// the two LLM services build one per instance. If the attempt count lived on the
		// pipeline rather than in Polly's per-execution context, a second call would open
		// with a stretched timeout and a burst of beeps, and nothing would say so.
		var clock = new ImmediateClock();
		var pipeline = TransientRetry.Pipeline(retryCount: 2, TransientRetry.Transient(), clock);

		var first = new List<int>();
		var second = new List<int>();

		await Assert.ThrowsAsync<HttpRequestException>(async () =>
			await pipeline.ExecuteWithAttemptAsync<int>((attempt, _) =>
			{
				first.Add(attempt);
				throw new HttpRequestException("transient");
			}));

		await Assert.ThrowsAsync<HttpRequestException>(async () =>
			await pipeline.ExecuteWithAttemptAsync<int>((attempt, _) =>
			{
				second.Add(attempt);
				throw new HttpRequestException("transient");
			}));

		Assert.Equal([1, 2, 3], first);
		Assert.Equal([1, 2, 3], second);
	}

	[Fact]
	public async Task ConcurrentCallsThroughOnePipelineCountSeparately()
	{
		// OcrService's pipeline is static, and nothing stops two transcriptions overlapping.
		var clock = new ImmediateClock();
		var pipeline = TransientRetry.Pipeline(retryCount: 2, TransientRetry.Transient(), clock);

		var seen = new List<int>[8];
		await Task.WhenAll(Enumerable.Range(0, seen.Length).Select(async i =>
		{
			var mine = seen[i] = [];
			await Assert.ThrowsAsync<HttpRequestException>(async () =>
				await pipeline.ExecuteWithAttemptAsync<int>((attempt, _) =>
				{
					mine.Add(attempt);
					throw new HttpRequestException("transient");
				}));
		}));

		Assert.All(seen, s => Assert.Equal([1, 2, 3], s));
	}

	[Fact]
	public async Task CancellingTheCallerSToken_StopsBeforeAnyRetry()
	{
		var clock = new ImmediateClock();
		var pipeline = TransientRetry.Pipeline(retryCount: 3, TransientRetry.Transient(), clock);
		using var cts = new CancellationTokenSource();
		int runs = 0;

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
			await pipeline.ExecuteWithAttemptAsync<int>((_, token) =>
			{
				runs++;
				cts.Cancel();
				token.ThrowIfCancellationRequested();
				return ValueTask.FromResult(0);
			}, cts.Token));

		// The body's token is the caller's, and a cancelled caller is not retried.
		Assert.Equal(1, runs);
	}

	private sealed class StatusException(int status) : Exception("status")
	{
		public int Status { get; } = status;
	}

	/// <summary>
	/// Records the delay Polly asks for, then fires straight away, so the delay sequence
	/// can be asserted exactly instead of inferred from a stopwatch.
	/// </summary>
	private sealed class ImmediateClock : TimeProvider
	{
		private readonly List<TimeSpan> _delays = [];

		public IReadOnlyList<TimeSpan> Delays
		{
			get { lock (_delays) return _delays.ToArray(); }
		}

		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			lock (_delays) _delays.Add(dueTime);
			return base.CreateTimer(callback, state, TimeSpan.Zero, period);
		}
	}
}
