using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace CognitiveSupport;

/// <summary>
/// The retry shape every remote-service call in this assembly shares: a linear 500 ms
/// backoff — or the wait the service itself asked for, when it sent one — and the number of
/// the attempt in flight, which the bodies use to stretch their per-attempt timeout and — in
/// the three services that make a sound — to beep once more with each retry so the listener
/// can hear how deep in it is.
/// </summary>
/// <remarks>
/// The attempt number reaches the body as an argument, and lives for the rest of its
/// journey in Polly's own per-execution context. Nothing here holds call state, so a
/// pipeline is safe to build once and reuse — including from several calls at the same
/// time, which is what the speech and OCR services do.
/// </remarks>
internal static class TransientRetry
{
	private static readonly ResiliencePropertyKey<int> AttemptKey = new("Attempt");

	/// <summary>
	/// First delay between attempts. Polly's linear backoff multiplies it by the attempt
	/// number, giving 500 ms, 1 s, 1.5 s — the same sequence Polly 7's
	/// <c>Backoff.LinearBackoff(500 ms, factor: 1)</c> produced here.
	/// </summary>
	private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(500);

	/// <summary>
	/// The failures all five services agree are worth another attempt. Callers that
	/// recognise a further one — a status-carrying exception from their own SDK — add it
	/// to what this returns.
	/// <para>
	/// <see cref="TaskCanceledException"/> is here for the per-attempt timeout, which
	/// fires on its own token and so does earn a retry. Cancelling the token handed to
	/// <see cref="ExecuteWithAttemptAsync"/> stops everything at once instead, and
	/// reports it as a plain <see cref="OperationCanceledException"/> rather than the
	/// TaskCanceledException Polly 7 raised from its interrupted delay. Every caller
	/// catches the base type, so that is invisible; catching the derived type on one of
	/// these paths would not be.
	/// </para>
	/// </summary>
	internal static PredicateBuilder<object> Transient() =>
		new PredicateBuilder()
			.Handle<HttpRequestException>()
			.Handle<TimeoutRejectedException>()
			.Handle<TaskCanceledException>();

	/// <param name="retryCount">
	/// Retries allowed after the first try. Zero or less runs the body exactly once —
	/// Polly rejects a retry strategy configured for no retries, so none is added.
	/// </param>
	/// <param name="handles">
	/// The failures worth another attempt, normally <see cref="Transient"/> with the
	/// caller's own additions. Anything else fails fast, so a permanent error such as a
	/// bad API key is reported at once rather than after every retry has run its course.
	/// </param>
	/// <param name="timeProvider">
	/// Test seam only; null in production. Lets a test read back the delays Polly asked
	/// for instead of sitting through them.
	/// </param>
	internal static ResiliencePipeline Pipeline(
		int retryCount,
		PredicateBuilder<object> handles,
		TimeProvider? timeProvider = null)
	{
		if (retryCount <= 0)
			return ResiliencePipeline.Empty;

		var clock = timeProvider ?? TimeProvider.System;
		var builder = new ResiliencePipelineBuilder { TimeProvider = clock };

		return builder
			.AddRetry(new RetryStrategyOptions
			{
				ShouldHandle = handles,
				MaxRetryAttempts = retryCount,
				BackoffType = DelayBackoffType.Linear,
				Delay = FirstDelay,
				// A service that answered with a Retry-After gets the wait it asked for;
				// everything else returns null here and falls back to the linear backoff
				// above. This is the only part of the stack that listens to a rate limit
				// now that the OpenAI SDK's own retry loop is off (issue #318).
				//
				// Null is the fallback signal and TimeSpan.Zero is not — Polly reads zero as
				// a real delay of no time — so RetryAfterHint answers null for every wait
				// that is not longer than nothing.
				DelayGenerator = args => ValueTask.FromResult(
					RetryAfterHint.From(args.Outcome.Exception, clock.GetUtcNow())),
				OnRetry = args =>
				{
					// AttemptNumber counts the attempt that just failed, from zero, so the
					// try about to start is two ahead of it.
					args.Context.Properties.Set(AttemptKey, args.AttemptNumber + 2);
					return default;
				},
			})
			.Build();
	}

	/// <summary>
	/// Runs <paramref name="body"/> through the pipeline, telling it which attempt it is
	/// on — 1 for the first try, 2 for the first retry, and so on.
	/// </summary>
	internal static async Task<T> ExecuteWithAttemptAsync<T>(
		this ResiliencePipeline pipeline,
		Func<int, CancellationToken, ValueTask<T>> body,
		CancellationToken cancellationToken = default)
	{
		var context = ResilienceContextPool.Shared.Get(cancellationToken);
		try
		{
			return await pipeline.ExecuteAsync(
				static (ctx, state) => state(
					ctx.Properties.TryGetValue(AttemptKey, out int attempt) ? attempt : 1,
					ctx.CancellationToken),
				context,
				body).ConfigureAwait(false);
		}
		finally
		{
			// Returning it clears the attempt, so the next borrower of this pooled
			// context cannot start life partway through someone else's retries.
			ResilienceContextPool.Shared.Return(context);
		}
	}
}
