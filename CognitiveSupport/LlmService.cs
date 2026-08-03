using OpenAI.Chat;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Timeout;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace CognitiveSupport;

public class LlmService : ILlmService
{
	private readonly Dictionary<string, ChatClient> _chatClients;
	private readonly Dictionary<string, LlmModelConfig> _modelConfigs;
	private readonly int _timeoutSeconds;
	private readonly int _retryCount;

	/// <param name="transport">
	/// Test seam only; null in production. See <see cref="OpenAiClientOptionsFactory.Create"/>.
	/// </param>
	public LlmService(
		string apiKey,
		IEnumerable<LlmModelConfig> models,
		int timeoutSeconds = 60,
		int retryCount = 3,
		PipelineTransport? transport = null)
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
				OpenAiClientOptionsFactory.Create(transport: transport));
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

		requestOptions ??= LlmRequestOptions.Default;
		bool fastMode = requestOptions.FastMode;

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

		async Task<ClientResult<ChatCompletion>> SendAsync(bool useFastMode)
		{
			ChatCompletionOptions options = BuildChatOptions(config, useFastMode);
			var context = new Context { [AttemptKey] = 1 };
			return await retryPolicy.ExecuteAsync(async (ctx) =>
			{
				int attempt = ctx.ContainsKey(AttemptKey) ? (int)ctx[AttemptKey] : 1;
				int timeout = _timeoutSeconds * attempt;
				using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
				return await client.CompleteChatAsync(openAiMessages, options, timeoutCts.Token).ConfigureAwait(false);
			}, context).ConfigureAwait(false);
		}

		ClientResult<ChatCompletion> result = fastMode
			? await SendWithFastModeFallbackAsync(SendAsync, requestOptions).ConfigureAwait(false)
			: await SendAsync(useFastMode: false).ConfigureAwait(false);

		if (fastMode)
			LogServedTier(result.Value);

		if (result.Value.Content.Count > 0)
		{
			return result.Value.Content[0].Text;
		}
		return string.Empty;
	}

	/// <summary>
	/// Mirrors the Anthropic service: if the request failed *because* of the fast service
	/// tier, retry once with the tier omitted rather than losing the user's text. §8 of the
	/// story scopes this to Anthropic, but its reasoning is provider-neutral — dropping the
	/// tier is always the cheaper direction, so it cannot surprise anyone's bill, and losing
	/// a long dictated transcript to a rejected tier value is far worse than a slower reply.
	/// </summary>
	private static async Task<ClientResult<ChatCompletion>> SendWithFastModeFallbackAsync(
		Func<bool, Task<ClientResult<ChatCompletion>>> send,
		LlmRequestOptions requestOptions)
	{
		FastModeFallback fallback;
		try
		{
			return await send(true).ConfigureAwait(false);
		}
		catch (ClientResultException ex)
		{
			// Only a rejected tier is handled here. Unlike Anthropic, OpenAI Fast mode has
			// no capacity pool or rate limit of its own, so a 429 is the account's ordinary
			// limit — dropping the tier would not get the user served any sooner, and the
			// SDK's own retry policy already had a go at it.
			var reason = FastModeFailure.Classify(ex.Status, ex.Message);
			if (reason is null)
				throw; // Nothing to do with the service tier; report it as-is.
			fallback = new FastModeFallback(reason.Value, ex.Message);
		}

		ErrorLogger.LogInfo("LLM", FastModeMessages.DescribeForLog(fallback));
		var result = await send(false).ConfigureAwait(false);

		// Announced only after the standard-speed retry succeeded, so the user is never
		// told "it ran at standard speed" for a request that then failed outright.
		requestOptions.OnFastModeFallback?.Invoke(fallback);
		return result;
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
