namespace Mutation.Tests;

/// <summary>
/// The JSON Deepgram answers a failed request with, copied from its published error
/// reference. Kept verbatim rather than paraphrased: the whole of issue #316 is what the
/// service can tell from that answer, so a body invented here would prove nothing.
/// </summary>
internal static class DeepgramErrorBodies
{
	public const string BadRequest =
		"""{"err_code":"Bad Request","err_msg":"Bad Request","request_id":"a1"}""";

	public const string InvalidAuth =
		"""{"err_code":"INVALID_AUTH","err_msg":"Invalid credentials.","request_id":"a2"}""";

	public const string PayloadTooLarge =
		"""{"err_code":"PAYLOAD_TOO_LARGE","err_msg":"Payload size exceeds limit of 2 MB.","request_id":"a3"}""";

	public const string TooManyRequests =
		"""{"err_code":"TOO_MANY_REQUESTS","err_msg":"Too many requests. Please try again later","request_id":"a4"}""";

	/// <summary>The same 429, spelled the way Deepgram's text-to-speech side spells it.</summary>
	public const string TooManyRequestsInWords =
		"""{"err_code":"Too Many Requests","err_msg":"Please try again later.","request_id":"a5"}""";

	/// <summary>
	/// A 503. Note <c>error_code</c>, not <c>err_code</c> — the field Deepgram's own client
	/// reads is missing, so nothing identifies this as a 503 by the time it reaches us.
	/// </summary>
	public const string ServiceUnavailable =
		"""{"error_code":"Service Unavailable","err_msg":"Please try again later","request_id":"a6"}""";
}
