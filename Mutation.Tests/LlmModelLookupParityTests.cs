using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http;
using CognitiveSupport;

namespace Mutation.Tests;

// The two ILlmService implementations used to disagree about what a model name means:
// OpenAI keyed its lookup ordinally, Anthropic case-insensitively, and a name repeated in
// Mutation.json was silently deduped by one and thrown on by the other with a framework
// message. Same file, same typo, two outcomes (issue #240).
//
// These tests drive both implementations through the same cases so the two cannot drift
// apart again.
public class LlmModelLookupParityTests : IDisposable
{
	private const string OpenAiModel = "gpt-4.1";
	private const string AnthropicModel = "claude-opus-5";

	private const string OpenAiSuccessBody =
		"""{"id":"c1","object":"chat.completion","created":1,"model":"gpt-4.1","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"done"}}]}""";
	private const string AnthropicSuccessBody = """{"content":[{"type":"text","text":"done"}]}""";

	// One client per test, all released at the end of the class rather than left to the
	// finalizer. Disposing the client disposes the fake handler with it.
	private readonly List<HttpClient> _clients = new();

	public void Dispose()
	{
		foreach (var client in _clients)
			client.Dispose();
	}

	private static LlmModelConfig Model(string name, LlmProvider provider) =>
		new(name, provider, customTemperature: null);

	private static IList<LlmChatMessage> Messages() =>
		new List<LlmChatMessage> { new(LlmChatRole.User, "hello") };

	private HttpClient Client(string successBody)
	{
		var client = new HttpClient(new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, successBody));
		_clients.Add(client);
		return client;
	}

	private AnthropicLlmService CreateAnthropic(params LlmModelConfig[] models) =>
		new("test-key", models, Client(AnthropicSuccessBody), timeoutSeconds: 5, retryCount: 0);

	private LlmService CreateOpenAi(params LlmModelConfig[] models) =>
		new("test-key", models, timeoutSeconds: 5, retryCount: 0,
			transport: new HttpClientPipelineTransport(Client(OpenAiSuccessBody)));

	// ----- Case-insensitive lookup -----

	[Fact]
	public async Task OpenAi_ModelNameDifferingOnlyInCase_Resolves()
	{
		var service = CreateOpenAi(Model(OpenAiModel, LlmProvider.OpenAI));

		string result = await service.CreateChatCompletion(Messages(), OpenAiModel.ToUpperInvariant());

		Assert.Equal("done", result);
	}

	[Fact]
	public async Task Anthropic_ModelNameDifferingOnlyInCase_Resolves()
	{
		var service = CreateAnthropic(Model(AnthropicModel, LlmProvider.Anthropic));

		string result = await service.CreateChatCompletion(Messages(), AnthropicModel.ToUpperInvariant());

		Assert.Equal("done", result);
	}

	[Fact]
	public async Task OpenAi_UnknownModel_IsStillRejected()
	{
		var service = CreateOpenAi(Model(OpenAiModel, LlmProvider.OpenAI));

		var thrown = await Assert.ThrowsAsync<ArgumentException>(
			() => service.CreateChatCompletion(Messages(), "no-such-model"));

		Assert.Equal("llmModelName", thrown.ParamName);
	}

	[Fact]
	public async Task Anthropic_UnknownModel_IsStillRejected()
	{
		var service = CreateAnthropic(Model(AnthropicModel, LlmProvider.Anthropic));

		var thrown = await Assert.ThrowsAsync<ArgumentException>(
			() => service.CreateChatCompletion(Messages(), "no-such-model"));

		Assert.Equal("llmModelName", thrown.ParamName);
	}

	// ----- Duplicate names -----

	[Fact]
	public void OpenAi_DuplicateModelName_ThrowsNamingTheDuplicate()
	{
		var thrown = Assert.Throws<ArgumentException>(() => CreateOpenAi(
			Model(OpenAiModel, LlmProvider.OpenAI),
			Model(OpenAiModel, LlmProvider.OpenAI)));

		Assert.Contains(OpenAiModel, thrown.Message);
		Assert.Equal("models", thrown.ParamName);
	}

	[Fact]
	public void Anthropic_DuplicateModelName_ThrowsNamingTheDuplicate()
	{
		var thrown = Assert.Throws<ArgumentException>(() => CreateAnthropic(
			Model(AnthropicModel, LlmProvider.Anthropic),
			Model(AnthropicModel, LlmProvider.Anthropic)));

		Assert.Contains(AnthropicModel, thrown.Message);
		Assert.Equal("models", thrown.ParamName);
	}

	// A duplicate that differs only in case is the same duplicate, since that is how the
	// lookup now reads it.
	[Fact]
	public void OpenAi_DuplicateDifferingOnlyInCase_Throws()
	{
		Assert.Throws<ArgumentException>(() => CreateOpenAi(
			Model(OpenAiModel, LlmProvider.OpenAI),
			Model(OpenAiModel.ToUpperInvariant(), LlmProvider.OpenAI)));
	}

	[Fact]
	public void Anthropic_DuplicateDifferingOnlyInCase_Throws()
	{
		Assert.Throws<ArgumentException>(() => CreateAnthropic(
			Model(AnthropicModel, LlmProvider.Anthropic),
			Model(AnthropicModel.ToUpperInvariant(), LlmProvider.Anthropic)));
	}

	// ----- Empty and null model sets -----

	[Fact]
	public void OpenAi_NoModels_Throws()
	{
		Assert.Equal("models", Assert.Throws<ArgumentException>(() => CreateOpenAi()).ParamName);
	}

	[Fact]
	public void Anthropic_NoModels_Throws()
	{
		Assert.Equal("models", Assert.Throws<ArgumentException>(() => CreateAnthropic()).ParamName);
	}

	[Fact]
	public void OpenAi_NullModels_Throws()
	{
		Assert.Equal("models", Assert.Throws<ArgumentNullException>(() => CreateOpenAi(null!)).ParamName);
	}

	[Fact]
	public void Anthropic_NullModels_Throws()
	{
		Assert.Equal("models", Assert.Throws<ArgumentNullException>(() => CreateAnthropic(null!)).ParamName);
	}
}
