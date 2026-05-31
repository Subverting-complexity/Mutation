namespace CognitiveSupport;

/// <summary>
/// Thrown for language-model API failures that are NOT worth retrying — chiefly 4xx
/// client errors such as 401 Unauthorized / 403 Forbidden (bad or missing API key),
/// 400 Bad Request, or 404 Not Found. The retry policies deliberately do not handle
/// this exception type, so it surfaces immediately instead of after several pointless
/// retries (which, with escalating timeouts, could otherwise make a simple bad-key
/// error take a minute or more to report).
/// </summary>
public class NonTransientLlmException : Exception
{
	public int StatusCode { get; }

	public NonTransientLlmException(string message, int statusCode)
		: base(message)
	{
		StatusCode = statusCode;
	}
}

internal static class LlmHttpStatus
{
	/// <summary>
	/// True for HTTP statuses worth retrying on cold start: connection failures (0,
	/// i.e. no response received), request timeout (408), rate limiting (429), and any
	/// 5xx server error. Other 4xx statuses (401/403 auth, 400 bad request, 404) are
	/// permanent for a given request and must not be retried.
	/// </summary>
	public static bool IsTransient(int statusCode)
		=> statusCode == 0
		|| statusCode == 408
		|| statusCode == 429
		|| statusCode >= 500;
}
