using Deepgram.Models.Exceptions.v1;

namespace CognitiveSupport;

/// <summary>
/// Whether a failure Deepgram answered with is worth another attempt.
/// <para>
/// Every failure Deepgram <em>answers</em> arrives as <see cref="DeepgramException"/> or its
/// <see cref="DeepgramRESTException"/> subclass, neither of which the shared transient set
/// handles. So a 429 rate limit or a 503 — exactly the failures another attempt would get
/// past — used to be reported to the user on the first try, with the recording already made
/// and the retry ladder never entered (issue #316).
/// </para>
/// <para>
/// The exception carries no HTTP status. What it does carry is
/// <see cref="DeepgramException.ErrCode"/>, which the SDK fills in from the <c>err_code</c>
/// field of Deepgram's JSON error body — so the decision is made on that instead.
/// </para>
/// <para>
/// Kept as a pure code-in, answer-out unit because the alternative is a decision buried in a
/// Polly predicate that only a live rate limit would exercise.
/// </para>
/// </summary>
internal static class DeepgramFailure
{
	/// <summary>
	/// The error codes Deepgram documents for a request that will fail the same way however
	/// many times it is sent: a rejected key, a malformed request, an unpayable account, audio
	/// it cannot read. Retrying any of these makes the user wait out the whole ladder on a
	/// lengthening timeout to be told what was already known on the first try.
	/// <para>
	/// Deepgram spells some of these twice — <c>PAYLOAD_TOO_LARGE</c> on one endpoint and
	/// <c>Payload Too Large</c> on another — so the comparison strips the punctuation and case
	/// rather than listing every spelling.
	/// </para>
	/// </summary>
	private static readonly HashSet<string> Permanent = new(StringComparer.Ordinal)
	{
		Normalized("Bad Request"),                 // 400
		Normalized("INVALID_QUERY_PARAMETER"),     // 400
		Normalized("PAYLOAD_ERROR"),               // 400
		Normalized("REMOTE_CONTENT_ERROR"),        // 400
		Normalized("INVALID_AUTH"),                // 401
		Normalized("ASR_PAYMENT_REQUIRED"),        // 402
		Normalized("INSUFFICIENT_PERMISSIONS"),    // 401 or 403
		Normalized("NOT_FOUND"),                   // 404
		Normalized("PROJECT_NOT_FOUND"),           // 404
		Normalized("PAYLOAD_TOO_LARGE"),           // 413
		Normalized("UNSUPPORTED_MEDIA_TYPE"),      // 415
		Normalized("UNPROCESSABLE_ENTITY"),        // 422
		Normalized("ASR_UNPROCESSABLE_ENTITY"),    // 422
	};

	/// <summary>
	/// True for a Deepgram failure another attempt could get past.
	/// <para>
	/// A list of what to give up on rather than a list of what to retry, and deliberately so.
	/// The recording is already made by the time this is asked, and losing it to an error
	/// Deepgram happened to spell in a way nobody here anticipated is a worse outcome than
	/// three needless attempts. It also covers the two failures whose code does not reach us:
	/// a 503 answers with <c>error_code</c> rather than <c>err_code</c>, which the SDK does
	/// not read, and a gateway that answers with an HTML page at all gives the SDK nothing to
	/// read, leaving its own "Unknown Error Code" placeholder behind. Both are transient, and
	/// both would be lost by an allow-list.
	/// </para>
	/// <para>
	/// The one documented client error this lets through is <c>INVALID_JSON</c> (400), whose
	/// body carries <c>category</c> instead of <c>err_code</c>. It answers a malformed JSON
	/// request body, and this app uploads audio bytes, so it is not a failure this caller can
	/// provoke.
	/// </para>
	/// </summary>
	public static bool IsWorthAnotherAttempt(DeepgramException failure) =>
		!Permanent.Contains(Normalized(failure?.ErrCode));

	/// <summary>
	/// An error code with its case and separators taken off, so <c>TOO_MANY_REQUESTS</c> and
	/// <c>Too Many Requests</c> are the one code they describe.
	/// </summary>
	private static string Normalized(string? errorCode)
	{
		if (string.IsNullOrWhiteSpace(errorCode))
			return string.Empty;

		return string.Concat(errorCode.Where(char.IsLetterOrDigit)).ToUpperInvariant();
	}
}
