using System.ClientModel;
using System.Globalization;

namespace CognitiveSupport;

/// <summary>
/// The wait a service asked for when it turned a request away, read off the
/// <c>Retry-After</c> header it answered with.
/// <para>
/// This exists because <see cref="OpenAiClientOptionsFactory"/> turned the OpenAI SDK's own
/// retry loop off (issue #318). That loop was the only part of the stack that listened to a
/// rate limit, so the listening had to move here — otherwise answering a 429 would mean
/// asking again on a flat 500 ms backoff, which is the wrong reply to being told to slow
/// down.
/// </para>
/// </summary>
internal static class RetryAfterHint
{
	/// <summary>
	/// Longest wait honoured. A service asking for minutes is asking for more than a user
	/// standing over a finished recording can give it; the ladder has a bounded number of
	/// attempts either way, so the choice is between waiting this long and reporting the
	/// failure, not between waiting this long and waiting the whole hour.
	/// </summary>
	internal static readonly TimeSpan Cap = TimeSpan.FromSeconds(60);

	/// <summary>
	/// The wait <paramref name="exception"/> asked for, or null when it asked for none —
	/// which is every failure that is not an answered HTTP error, and most of those too.
	/// Null leaves the caller's own backoff in charge.
	/// </summary>
	internal static TimeSpan? From(Exception? exception, DateTimeOffset now) =>
		exception is ClientResultException answered
			? Parse(HeaderOf(answered), now)
			: null;

	private static string? HeaderOf(ClientResultException answered)
	{
		var response = answered.GetRawResponse();
		if (response is null)
			return null;

		return response.Headers.TryGetValue("Retry-After", out string? value) ? value : null;
	}

	/// <summary>
	/// RFC 9110 allows either a count of seconds or an HTTP date; OpenAI sends seconds, but
	/// a proxy in front of it need not. Anything else is ignored rather than guessed at.
	/// <para>
	/// Only a wait longer than nothing is answered with. Zero, a negative count, and a date
	/// that has already passed all mean "no wait", and the caller reads null as "no opinion"
	/// and keeps its own backoff — which is the whole point, because Polly treats a returned
	/// <see cref="TimeSpan.Zero"/> as a real delay of no time at all. Honouring it would run
	/// the entire ladder back-to-back in a few milliseconds, on the say-so of a header the
	/// server controls: the exact hammering this change was made to stop.
	/// </para>
	/// </summary>
	internal static TimeSpan? Parse(string? headerValue, DateTimeOffset now)
	{
		if (string.IsNullOrWhiteSpace(headerValue))
			return null;

		string text = headerValue.Trim();
		TimeSpan wait;

		// Seconds first: "120" parses as a date in some cultures' formats, never the other
		// way round, so the numeric reading has to win to be reachable at all.
		if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
		{
			// "NaN" and "Infinity" are words double.TryParse accepts, and a number too big
			// for a double overflows to infinity rather than failing to parse. None of them
			// is a wait; they are unreadable, like "soon".
			if (!double.IsFinite(seconds) || seconds <= 0d)
				return null;
			// Compared before converting: TimeSpan.FromSeconds overflows on a large enough
			// finite number, and a header is free to carry one.
			if (seconds >= Cap.TotalSeconds)
				return Cap;
			wait = TimeSpan.FromSeconds(seconds);
		}
		else if (DateTimeOffset.TryParse(
			text,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
			out var when))
		{
			wait = when - now;
		}
		else
		{
			return null;
		}

		if (wait <= TimeSpan.Zero)
			return null;

		return wait > Cap ? Cap : wait;
	}
}
