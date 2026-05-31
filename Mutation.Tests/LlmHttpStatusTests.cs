using CognitiveSupport;

namespace Mutation.Tests;

// Verifies the transient/non-transient classification that decides whether the LLM
// retry policies retry a failed request. Permanent 4xx errors (esp. 401 bad API key)
// must NOT be retried; rate limiting and server errors must be.
public class LlmHttpStatusTests
{
	[Theory]
	[InlineData(0)]    // connection failure / no response
	[InlineData(408)]  // request timeout
	[InlineData(429)]  // rate limited
	[InlineData(500)]  // server error
	[InlineData(503)]  // service unavailable
	public void Transient_StatusCodes_AreRetryable(int statusCode)
	{
		Assert.True(LlmHttpStatus.IsTransient(statusCode));
	}

	[Theory]
	[InlineData(400)]  // bad request
	[InlineData(401)]  // unauthorized (bad/missing API key)
	[InlineData(403)]  // forbidden
	[InlineData(404)]  // not found
	[InlineData(422)]  // unprocessable entity
	public void Permanent_ClientErrors_AreNotRetryable(int statusCode)
	{
		Assert.False(LlmHttpStatus.IsTransient(statusCode));
	}
}
