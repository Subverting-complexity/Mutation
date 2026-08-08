using System.Text.Json;
using CognitiveSupport;
using Deepgram.Models.Exceptions.v1;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="DeepgramFailure"/> — which of Deepgram's own answers earn another
/// attempt.
/// <para>
/// Deepgram's exception carries no HTTP status, so the decision is made on the
/// <c>err_code</c> its JSON error body carries. Everything here is built the way the SDK
/// builds it, by deserializing a published error body into the exception; an exception
/// constructed by hand has no error code on it and would agree with any rule at all
/// (issue #316).
/// </para>
/// </summary>
public class DeepgramFailureTests
{
	private static DeepgramException Answered(string errorBody) =>
		JsonSerializer.Deserialize<DeepgramRESTException>(errorBody)!;

	[Fact]
	public void A_rate_limit_is_worth_another_attempt()
	{
		Assert.True(DeepgramFailure.IsWorthAnotherAttempt(Answered(DeepgramErrorBodies.TooManyRequests)));
	}

	[Fact]
	public void A_rate_limit_spelled_in_words_is_the_same_rate_limit()
	{
		// Deepgram writes this code both ways depending on the endpoint. Listing one spelling
		// and missing the other would retry half the rate limits and report the rest.
		Assert.True(DeepgramFailure.IsWorthAnotherAttempt(Answered(DeepgramErrorBodies.TooManyRequestsInWords)));
	}

	[Fact]
	public void An_unavailable_service_is_worth_another_attempt()
	{
		// The 503 body names its code in a field Deepgram's client does not read, so this
		// arrives with no usable code at all. Giving up on it would abandon a finished
		// recording over an outage that is over by the next attempt.
		Assert.True(DeepgramFailure.IsWorthAnotherAttempt(Answered(DeepgramErrorBodies.ServiceUnavailable)));
	}

	[Fact]
	public void An_answer_that_is_not_Deepgram_JSON_at_all_is_worth_another_attempt()
	{
		// What a gateway in front of Deepgram answers with when Deepgram is not answering.
		// The client cannot read it, so it raises the base exception with its own placeholder
		// code.
		Assert.True(DeepgramFailure.IsWorthAnotherAttempt(
			new DeepgramException("<html><body>502 Bad Gateway</body></html>")));
	}

	[Theory]
	[InlineData("Bad Request")]
	[InlineData("INVALID_QUERY_PARAMETER")]
	[InlineData("PAYLOAD_ERROR")]
	[InlineData("REMOTE_CONTENT_ERROR")]
	[InlineData("INVALID_AUTH")]
	[InlineData("ASR_PAYMENT_REQUIRED")]
	[InlineData("INSUFFICIENT_PERMISSIONS")]
	[InlineData("PROJECT_NOT_FOUND")]
	[InlineData("PAYLOAD_TOO_LARGE")]
	[InlineData("UNSUPPORTED_MEDIA_TYPE")]
	[InlineData("UNPROCESSABLE_ENTITY")]
	[InlineData("ASR_UNPROCESSABLE_ENTITY")]
	public void A_request_that_will_fail_the_same_way_again_is_not_retried(string errorCode)
	{
		string body = $$"""{"err_code":"{{errorCode}}","err_msg":"no","request_id":"x"}""";

		Assert.False(DeepgramFailure.IsWorthAnotherAttempt(Answered(body)));
	}

	[Fact]
	public void A_rejected_key_is_reported_at_once_however_the_code_is_punctuated()
	{
		// The comparison strips case and separators, so a future spelling of a code already
		// known to be permanent still fails fast.
		Assert.False(DeepgramFailure.IsWorthAnotherAttempt(
			Answered("""{"err_code":"invalid auth","err_msg":"no","request_id":"x"}""")));
	}

	[Fact]
	public void A_payload_too_large_is_final_whichever_way_it_is_written()
	{
		Assert.False(DeepgramFailure.IsWorthAnotherAttempt(Answered(DeepgramErrorBodies.PayloadTooLarge)));
		Assert.False(DeepgramFailure.IsWorthAnotherAttempt(
			Answered("""{"err_code":"Payload Too Large","err_msg":"no","request_id":"x"}""")));
	}

	[Fact]
	public void A_code_nobody_here_has_seen_is_given_another_attempt()
	{
		// The list says what to give up on, not what to retry. Losing a recording to a code
		// nobody anticipated is worse than three needless attempts.
		Assert.True(DeepgramFailure.IsWorthAnotherAttempt(
			Answered("""{"err_code":"SOMETHING_NEW","err_msg":"?","request_id":"x"}""")));
	}

	[Fact]
	public void A_missing_failure_is_not_treated_as_permanent()
	{
		Assert.True(DeepgramFailure.IsWorthAnotherAttempt(null!));
	}
}
