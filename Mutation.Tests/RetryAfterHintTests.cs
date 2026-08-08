using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Issue #318: turning the OpenAI SDK's own retry loop off took the only part of the stack
/// that read <c>Retry-After</c> with it. These pin the reading that replaced it.
/// </summary>
public class RetryAfterHintTests
{
	private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public void A_count_of_seconds_is_the_wait()
	{
		// What OpenAI actually sends.
		Assert.Equal(TimeSpan.FromSeconds(2), RetryAfterHint.Parse("2", Now));
	}

	[Fact]
	public void Fractional_seconds_are_kept()
	{
		Assert.Equal(TimeSpan.FromSeconds(1.5), RetryAfterHint.Parse("1.5", Now));
	}

	[Fact]
	public void Surrounding_space_does_not_hide_the_number()
	{
		Assert.Equal(TimeSpan.FromSeconds(3), RetryAfterHint.Parse("  3  ", Now));
	}

	[Fact]
	public void An_http_date_is_read_as_the_wait_until_then()
	{
		Assert.Equal(TimeSpan.FromSeconds(30), RetryAfterHint.Parse("Sat, 08 Aug 2026 12:00:30 GMT", Now));
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-5")]
	[InlineData("Sat, 08 Aug 2026 11:59:00 GMT")] // already past
	[InlineData("Sat, 08 Aug 2026 12:00:00 GMT")] // exactly now
	public void A_wait_of_no_time_at_all_leaves_the_backoff_in_charge(string header)
	{
		// Never TimeSpan.Zero. Polly reads a returned zero as a real delay of no time, which
		// would run the whole ladder back-to-back in a few milliseconds — on the say-so of a
		// header the server sends. Null is what puts the linear backoff back in charge.
		Assert.Null(RetryAfterHint.Parse(header, Now));
	}

	[Theory]
	[InlineData("3600")]
	[InlineData("Sat, 08 Aug 2026 13:00:00 GMT")]
	public void A_wait_longer_than_the_cap_is_cut_to_it(string header)
	{
		// A user is standing over a finished recording. An hour is not a wait; it is a
		// failure that has not been reported yet.
		Assert.Equal(RetryAfterHint.Cap, RetryAfterHint.Parse(header, Now));
	}

	[Fact]
	public void A_number_too_big_for_a_TimeSpan_is_cut_to_the_cap_rather_than_overflowing()
	{
		// TimeSpan.FromSeconds throws on a number this size, so the cap has to be applied
		// before the conversion rather than after it.
		Assert.Equal(RetryAfterHint.Cap, RetryAfterHint.Parse("1000000000000000000", Now));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("soon")]
	[InlineData("NaN")]
	[InlineData("Infinity")]
	[InlineData("1E400")] // overflows a double, so it arrives as infinity rather than failing
	public void Anything_unreadable_leaves_the_backoff_in_charge(string? header)
	{
		// Null is the "no opinion" answer, and it is what puts the caller's own linear
		// backoff back in charge — not a zero wait, which would retry at once.
		Assert.Null(RetryAfterHint.Parse(header, Now));
	}

	[Fact]
	public void A_failure_that_is_not_an_answered_http_error_asks_for_nothing()
	{
		Assert.Null(RetryAfterHint.From(new HttpRequestException("connection reset"), Now));
		Assert.Null(RetryAfterHint.From(null, Now));
	}
}
