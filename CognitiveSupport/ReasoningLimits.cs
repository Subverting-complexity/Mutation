namespace CognitiveSupport;

/// <summary>
/// The answer limit and wait time a request gets at a reasoning level. Heavier reasoning
/// spends more tokens thinking and takes longer, so a request at a heavy level with the
/// light-level limits would be cut off or timed out before it answers.
/// <para>
/// The values are fixed here and documented in the user guide. The Anthropic answer
/// limit stops at 16,000 because Mutation sends each request whole and does not stream
/// it, and the Anthropic service refuses larger whole-request limits that it expects to
/// run for a very long time.
/// </para>
/// </summary>
/// <param name="AnthropicMaxTokens">The <c>max_tokens</c> an Anthropic request sends.</param>
/// <param name="WaitMultiplier">How many times the user's wait time each try may take.</param>
public sealed record ReasoningLimits(int AnthropicMaxTokens, int WaitMultiplier)
{
	private static readonly ReasoningLimits Light = new(8_000, 1);
	private static readonly ReasoningLimits MediumLimits = new(12_000, 2);
	private static readonly ReasoningLimits HighLimits = new(16_000, 3);
	private static readonly ReasoningLimits Heaviest = new(16_000, 4);

	/// <summary>The limits for a resolved level; null (no level sent) gets the lightest limits.</summary>
	public static ReasoningLimits For(ReasoningLevel? level) =>
		level switch
		{
			ReasoningLevel.Medium => MediumLimits,
			ReasoningLevel.High => HighLimits,
			ReasoningLevel.ExtraHigh or ReasoningLevel.Max => Heaviest,
			_ => Light,
		};

	/// <summary>The user's wait time scaled for this level.</summary>
	/// <param name="userWait">The wait time per try the user set.</param>
	public TimeSpan WaitPerTry(TimeSpan userWait) => userWait * WaitMultiplier;
}
