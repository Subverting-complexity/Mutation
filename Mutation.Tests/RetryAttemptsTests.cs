using System.Diagnostics;
using CognitiveSupport;
using Polly;
using Polly.Timeout;

namespace Mutation.Tests;

// Issue #259: the five remote-service callers moved off Polly 7's Policy/Context API onto
// Polly 8's resilience pipelines. Every one of them scales its per-attempt timeout by the
// attempt number and beeps once per retry, so the attempt count and the delay sequence are
// behaviour, not implementation detail. These tests pin both to what Polly 7 produced.
public class RetryAttemptsTests
{
	private static PredicateBuilder<object> Transient() =>
		new PredicateBuilder()
			.Handle<HttpRequestException>()
			.Handle<TimeoutRejectedException>()
			.Handle<TaskCanceledException>();

	[Fact]
	public async Task FirstTryRunsAsAttemptOne()
	{
		var retry = new RetryAttempts(retryCount: 3, Transient());
		var seen = new List<int>();

		await retry.Pipeline.ExecuteAsync(_ =>
		{
			seen.Add(retry.Attempt);
			return ValueTask.CompletedTask;
		});

		Assert.Equal([1], seen);
	}

	[Fact]
	public async Task EachRetryAdvancesTheAttemptNumber()
	{
		var retry = new RetryAttempts(retryCount: 2, Transient());
		var seen = new List<int>();

		await Assert.ThrowsAsync<HttpRequestException>(async () =>
			await retry.Pipeline.ExecuteAsync(_ =>
			{
				seen.Add(retry.Attempt);
				throw new HttpRequestException("transient");
			}));

		// Three runs in all: the first try plus the two retries, each one attempt higher so
		// the caller's timeout keeps stretching.
		Assert.Equal([1, 2, 3], seen);
	}

	[Fact]
	public async Task NoRetriesConfigured_StillRunsTheBodyOnce()
	{
		// LlmService and AnthropicLlmService both allow a retry count of zero, and Polly 8
		// rejects a retry strategy built for no retries — so no strategy is added at all.
		var retry = new RetryAttempts(retryCount: 0, Transient());
		int runs = 0;

		await Assert.ThrowsAsync<HttpRequestException>(async () =>
			await retry.Pipeline.ExecuteAsync(_ =>
			{
				runs++;
				throw new HttpRequestException("transient");
			}));

		Assert.Equal(1, runs);
		Assert.Equal(1, retry.Attempt);
	}

	[Fact]
	public async Task NegativeRetryCountIsTreatedAsNone()
	{
		var retry = new RetryAttempts(retryCount: -5, Transient());
		int runs = 0;

		await retry.Pipeline.ExecuteAsync(_ =>
		{
			runs++;
			return ValueTask.CompletedTask;
		});

		Assert.Equal(1, runs);
	}

	[Fact]
	public async Task UnhandledFailureIsNotRetried()
	{
		// A bad API key has to surface at once rather than after every retry has run.
		var retry = new RetryAttempts(retryCount: 3, Transient());
		int runs = 0;

		await Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await retry.Pipeline.ExecuteAsync(_ =>
			{
				runs++;
				throw new InvalidOperationException("permanent");
			}));

		Assert.Equal(1, runs);
	}

	[Fact]
	public async Task APredicateCanNarrowWhichFailuresAreRetried()
	{
		// OcrService and LlmService both retry a status-carrying exception only when the
		// status is transient.
		var retry = new RetryAttempts(
			retryCount: 3,
			new PredicateBuilder().Handle<StatusException>(ex => ex.Status >= 500));
		int runs = 0;

		await Assert.ThrowsAsync<StatusException>(async () =>
			await retry.Pipeline.ExecuteAsync(_ =>
			{
				runs++;
				throw new StatusException(400);
			}));

		Assert.Equal(1, runs);
	}

	[Fact]
	public async Task DelaysGrowLinearlyFromHalfASecond()
	{
		// Polly 7 produced 500 ms then 1 s here via Backoff.LinearBackoff(500 ms, factor: 1).
		// Polly 8's linear backoff has to land on the same sequence, or a cold start gets
		// noticeably less breathing room than it used to.
		var retry = new RetryAttempts(retryCount: 2, Transient());
		var stopwatch = Stopwatch.StartNew();

		await Assert.ThrowsAsync<HttpRequestException>(async () =>
			await retry.Pipeline.ExecuteAsync(_ => throw new HttpRequestException("transient")));

		stopwatch.Stop();
		// The lower bound allows a little slack for timer resolution; the upper bound is
		// what would catch an exponential or jittered backoff sneaking in.
		Assert.InRange(stopwatch.Elapsed.TotalMilliseconds, 1400, 4000);
	}

	private sealed class StatusException(int status) : Exception("status")
	{
		public int Status { get; } = status;
	}
}
