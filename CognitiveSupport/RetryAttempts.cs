using Polly;
using Polly.Retry;

namespace CognitiveSupport;

/// <summary>
/// The retry shape every remote-service call in this assembly shares: a linear 500 ms
/// backoff, plus the number of the attempt in flight so the body can stretch its own
/// timeout for each try and beep once per retry.
/// <para>
/// Create one per call. <see cref="Attempt"/> is the state of a single execution, so
/// sharing an instance would let one call's retries lengthen another call's timeout.
/// </para>
/// </summary>
internal sealed class RetryAttempts
{
	/// <summary>
	/// First delay between attempts. Polly's linear backoff multiplies it by the attempt
	/// number, giving 500 ms, 1 s, 1.5 s — the same sequence Polly 7's
	/// <c>Backoff.LinearBackoff(500 ms, factor: 1)</c> produced here.
	/// </summary>
	private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(500);

	private int _attempt = 1;

	/// <summary>1 while the first try runs, 2 during the first retry, and so on.</summary>
	internal int Attempt => _attempt;

	/// <summary>Execute the call through this.</summary>
	internal ResiliencePipeline Pipeline { get; }

	/// <param name="retryCount">
	/// Retries allowed after the first try. Zero or less runs the body exactly once —
	/// Polly rejects a retry strategy configured for no retries, so none is added.
	/// </param>
	/// <param name="handles">
	/// The transient failures worth another attempt. Anything else fails fast, so a
	/// permanent error such as a bad API key is reported at once rather than after
	/// every retry has run its course.
	/// </param>
	internal RetryAttempts(int retryCount, PredicateBuilder<object> handles)
	{
		Pipeline = retryCount <= 0
			? ResiliencePipeline.Empty
			: new ResiliencePipelineBuilder()
				.AddRetry(new RetryStrategyOptions
				{
					ShouldHandle = handles,
					MaxRetryAttempts = retryCount,
					BackoffType = DelayBackoffType.Linear,
					Delay = FirstDelay,
					OnRetry = _ =>
					{
						_attempt++;
						return default;
					},
				})
				.Build();
	}
}
