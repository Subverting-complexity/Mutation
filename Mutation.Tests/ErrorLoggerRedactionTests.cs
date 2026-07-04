using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// All tests in this class mutate ErrorLogger's static secret registry. xUnit
// runs tests within a single class sequentially, so each test sets the registry
// to a known state at the top and the tests stay order-independent.
public class ErrorLoggerRedactionTests
{
	[Fact]
	public void RedactSecrets_Null_ReturnsNull()
	{
		ErrorLogger.RegisterSecretValues(null);
		Assert.Null(ErrorLogger.RedactSecrets(null!));
	}

	[Fact]
	public void RedactSecrets_Empty_ReturnsEmpty()
	{
		ErrorLogger.RegisterSecretValues(null);
		Assert.Equal(string.Empty, ErrorLogger.RedactSecrets(string.Empty));
	}

	[Fact]
	public void RedactSecrets_OpenAiStyleKey_Redacted()
	{
		ErrorLogger.RegisterSecretValues(null);
		string input = "auth failed with key sk-abcdEFGH1234567890 at call site";

		string result = ErrorLogger.RedactSecrets(input);

		Assert.DoesNotContain("sk-abcdEFGH1234567890", result);
		Assert.Contains("sk-***REDACTED***", result);
	}

	[Fact]
	public void RedactSecrets_BearerToken_Redacted()
	{
		ErrorLogger.RegisterSecretValues(null);
		string input = "Authorization: Bearer eyJhbGciOiJ_secret-token.value";

		string result = ErrorLogger.RedactSecrets(input);

		Assert.DoesNotContain("eyJhbGciOiJ_secret-token.value", result);
		Assert.Contains("Bearer ***REDACTED***", result);
	}

	[Fact]
	public void RedactSecrets_DeepgramTokenHeader_RedactedByPattern()
	{
		// Deepgram is sent as `Authorization: Token <key>` — covered even when
		// the key was never registered.
		ErrorLogger.RegisterSecretValues(null);
		string deepgramKey = "0123456789abcdef0123456789abcdef01234567"; // 40-char hex
		string input = $"Authorization: Token {deepgramKey}";

		string result = ErrorLogger.RedactSecrets(input);

		Assert.DoesNotContain(deepgramKey, result);
		Assert.Contains("Token ***REDACTED***", result);
	}

	[Fact]
	public void RedactSecrets_RegisteredDeepgramKey_RedactedAnywhere()
	{
		// A bare 40-char hex Deepgram key with no header prefix matches no
		// pattern; exact-match registration must still redact it.
		string deepgramKey = "0123456789abcdef0123456789abcdef01234567";
		ErrorLogger.RegisterSecretValues(new[] { deepgramKey });
		string input = $"GET https://api.deepgram.com/v1/listen?key={deepgramKey} failed";

		string result = ErrorLogger.RedactSecrets(input);

		Assert.DoesNotContain(deepgramKey, result);
		Assert.Contains("***REDACTED***", result);
	}

	[Fact]
	public void RedactSecrets_RegisteredAzureKey_RedactedAnywhere()
	{
		// Azure Computer Vision keys are 32-char hex — no pattern matches them.
		string azureKey = "0123456789abcdef0123456789abcdef"; // 32-char hex
		ErrorLogger.RegisterSecretValues(new[] { azureKey });
		string input = $"Ocp-Apim-Subscription-Key header carried {azureKey} in the failure";

		string result = ErrorLogger.RedactSecrets(input);

		Assert.DoesNotContain(azureKey, result);
		Assert.Contains("***REDACTED***", result);
	}

	[Fact]
	public void RegisterSecretValues_IgnoresBlankPlaceholderAndShortValues()
	{
		// None of these should be registered; a short token like "shortkey"
		// must remain readable so real log text is not over-redacted.
		ErrorLogger.RegisterSecretValues(new[] { null, "", "   ", "<placeholder>", "short" });
		string input = "value <placeholder> and the word short appear here";

		string result = ErrorLogger.RedactSecrets(input);

		Assert.Equal(input, result);
	}

	[Fact]
	public void RegisterSecretValues_ReplacesPreviousSnapshot()
	{
		string oldKey = "OLD-secret-key-value-1234567890";
		string newKey = "NEW-secret-key-value-0987654321";

		ErrorLogger.RegisterSecretValues(new[] { oldKey });
		ErrorLogger.RegisterSecretValues(new[] { newKey });

		string result = ErrorLogger.RedactSecrets($"{oldKey} then {newKey}");

		// The old key is no longer tracked and appears verbatim; the new one is gone.
		Assert.Contains(oldKey, result);
		Assert.DoesNotContain(newKey, result);
	}

	[Fact]
	public void RegisterSecretValues_Null_ClearsRegistry()
	{
		string key = "some-registered-secret-value-1234";
		ErrorLogger.RegisterSecretValues(new[] { key });
		ErrorLogger.RegisterSecretValues(null);

		string result = ErrorLogger.RedactSecrets($"still has {key} in it");

		Assert.Contains(key, result);
	}
}
