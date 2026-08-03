using OpenAI.Chat;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Timeout;
using System.ClientModel;

namespace CognitiveSupport;

public class LlmService : ILlmService
{
	private readonly Dictionary<string, ChatClient> _chatClients;
	private readonly Dictionary<string, LlmModelConfig> _modelConfigs;
	private readonly int _timeoutSeconds;
	private readonly int _retryCount;

	public LlmService(
		string apiKey,
		IEnumerable<LlmModelConfig> models,
		int timeoutSeconds = 60,
		int retryCount = 3)
	{
		if (string.IsNullOrEmpty(apiKey)) throw new ArgumentNullException(nameof(apiKey));
		if (models is null) throw new ArgumentNullException(nameof(models));

		var modelList = models.ToList();
		if (modelList.Count == 0)
			throw new ArgumentException("At least one model must be configured.", nameof(models));

		_chatClients = new Dictionary<string, ChatClient>();
		_modelConfigs = new Dictionary<string, LlmModelConfig>();
		_timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 60;
		_retryCount = retryCount < 0 ? 0 : retryCount;

		foreach (var model in modelList)
		{
			// Options disable the SDK's 100 s default network timeout so the
			// escalating per-attempt timeouts below are the real authority.
			_chatClients[model.Name] = new ChatClient(
				model.Name,
				new ApiKeyCredential(apiKey),
				OpenAiClientOptionsFactory.Create());
			_modelConfigs[model.Name] = model;
		}
	}

	public async Task<string> CreateChatCompletion(
		IList<LlmChatMessage> messages,
		string llmModelName,
		LlmRequestOptions? requestOptions = null)
	{
		if (!_chatClients.ContainsKey(llmModelName))
			throw new ArgumentException($"{llmModelName} is not one of the configured models. The following are the available, configured models: {string.Join(",", _chatClients.Keys)}", nameof(llmModelName));

		var client = _chatClients[llmModelName];
		var config = _modelConfigs[llmModelName];

		var openAiMessages = messages.Select(ToOpenAiMessage).ToList();

		bool fastMode = (requestOptions ?? LlmRequestOptions.Default).FastMode;
		ChatCompletionOptions options = BuildChatOptions(config, fastMode);

		// Retry policy mirrors OpenAiSpeechToTextService.cs so the cold-start path (slow
		// DNS/TLS/JIT warmup on the first call after a reboot) gets a few escalating-timeout
		// attempts instead of an unhandled failure. If _retryCount == 0 the body still runs once.
		// The OpenAI SDK reports API errors as ClientResultException; only transient statuses
		// (429, 5xx, connection failures) are retried, so a permanent 4xx such as 401
		// Unauthorized (bad API key) fails fast instead of after every retry.
		const string AttemptKey = "Attempt";

		var delay = Backoff.LinearBackoff(TimeSpan.FromMilliseconds(500), retryCount: _retryCount, factor: 1);
		var retryPolicy = Policy
			.Handle<HttpRequestException>()
			.Or<TimeoutRejectedException>()
			.Or<TaskCanceledException>()
			.Or<ClientResultException>(ex => LlmHttpStatus.IsTransient(ex.Status))
				.WaitAndRetryAsync(
					delay,
					onRetry: (exception, timeSpan, attemptNumber, context) =>
					{
						int attempt = context.ContainsKey(AttemptKey) ? (int)context[AttemptKey] : 1;
						context[AttemptKey] = ++attempt;
					}
				);

		var pollyContext = new Context();
		pollyContext[AttemptKey] = 1;

		ClientResult<ChatCompletion> result = await retryPolicy.ExecuteAsync(async (ctx) =>
		{
			int attempt = ctx.ContainsKey(AttemptKey) ? (int)ctx[AttemptKey] : 1;
			int timeout = _timeoutSeconds * attempt;
			using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
			return await client.CompleteChatAsync(openAiMessages, options, timeoutCts.Token).ConfigureAwait(false);
		}, pollyContext).ConfigureAwait(false);

		if (fastMode)
			LogServedTier(result.Value);

		if (result.Value.Content.Count > 0)
		{
			return result.Value.Content[0].Text;
		}
		return string.Empty;
	}

	/// <summary>
	/// The single place chat request options are constructed.
	/// </summary>
	/// <param name="fastMode">
	/// Premium inference speed on the same model, at roughly twice the token price.
	/// Requested via the service tier; nothing is set when off, so the account default
	/// applies exactly as before.
	/// </param>
	internal static ChatCompletionOptions BuildChatOptions(LlmModelConfig config, bool fastMode = false)
	{
		var options = new ChatCompletionOptions();
		if (config.CustomTemperature.HasValue)
		{
			options.Temperature = (float)config.CustomTemperature.Value;
		}
		if (fastMode)
		{
			// ChatServiceTier is an extensible enum-like struct with no named "fast"
			// member; the raw string is the documented way to request Fast mode.
			// OPENAI001: ServiceTier is marked evaluation-only by the SDK. It is the only
			// way to request Fast mode, and the single line here is all that has to move
			// if the SDK renames it.
#pragma warning disable OPENAI001
			options.ServiceTier = FastServiceTier;
#pragma warning restore OPENAI001
		}
		return options;
	}

	internal const string FastServiceTier = "fast";

	/// <summary>
	/// OpenAI echoes back the tier it actually served — often "priority" (documented as
	/// behaviourally identical to fast) and "default" when it downgrades under load.
	/// Neither is an error, and a downgrade bills at standard rates, so there is nothing
	/// to warn the user about; record it for diagnosis and move on.
	/// </summary>
	private static void LogServedTier(ChatCompletion completion)
	{
#pragma warning disable OPENAI001 // Evaluation-only SDK surface; see BuildChatOptions.
		string? served = completion.ServiceTier?.ToString();
#pragma warning restore OPENAI001
		if (!string.IsNullOrEmpty(served) && served != FastServiceTier)
			ErrorLogger.LogInfo("LLM", $"OpenAI served service tier '{served}' for a Fast mode request.");
	}

	private static ChatMessage ToOpenAiMessage(LlmChatMessage msg)
		=> msg.Role switch
		{
			LlmChatRole.System => new SystemChatMessage(msg.Content),
			LlmChatRole.User => new UserChatMessage(msg.Content),
			LlmChatRole.Assistant => new AssistantChatMessage(msg.Content),
			_ => throw new ArgumentOutOfRangeException(nameof(msg.Role), msg.Role, "Unsupported chat role")
		};
}
