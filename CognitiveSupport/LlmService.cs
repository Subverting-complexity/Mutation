using OpenAI.Chat;
using Polly;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace CognitiveSupport;

public class LlmService : ILlmService
{
	private readonly Dictionary<string, ChatClient> _chatClients;
	private readonly Dictionary<string, LlmModelConfig> _modelConfigs;
	private readonly int _timeoutSeconds;
	private readonly ResiliencePipeline _retryPipeline;
	private readonly TimeProvider _timeProvider;

	/// <param name="transport">
	/// Test seam only; null in production. See <see cref="OpenAiClientOptionsFactory.Create"/>.
	/// </param>
	/// <param name="timeProvider">
	/// Test seam only; null in production. Drives both the retry backoff and each
	/// attempt's deadline, so a test can read back the waits that were armed instead of
	/// sitting through them.
	/// </param>
	public LlmService(
		string apiKey,
		IEnumerable<LlmModelConfig> models,
		int timeoutSeconds = 60,
		int retryCount = 3,
		PipelineTransport? transport = null,
		TimeProvider? timeProvider = null)
	{
		if (string.IsNullOrEmpty(apiKey)) throw new ArgumentNullException(nameof(apiKey));

		var modelList = LlmModelIndex.Validate(models, nameof(models));

		_chatClients = new Dictionary<string, ChatClient>(LlmModelIndex.NameComparer);
		_modelConfigs = new Dictionary<string, LlmModelConfig>(LlmModelIndex.NameComparer);
		_timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 60;
		_timeProvider = timeProvider ?? TimeProvider.System;

		// The OpenAI SDK reports API errors as ClientResultException; only transient
		// statuses (429, 5xx, connection failures) are retried, so a permanent 4xx such as
		// 401 Unauthorized (bad API key) fails fast instead of after every retry.
		_retryPipeline = TransientRetry.Pipeline(
			retryCount,
			TransientRetry.Transient()
				.Handle<ClientResultException>(ex => LlmHttpStatus.IsTransient(ex.Status)),
			timeProvider);

		foreach (var model in modelList)
		{
			// Options disable the SDK's 100 s default network timeout so the escalating
			// per-attempt timeouts below are the real authority, and its own retry loop so
			// the pipeline above is the only one counting (issue #318).
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
		LlmRequestOptions? requestOptions = null,
		CancellationToken cancellationToken = default)
	{
		if (!_chatClients.ContainsKey(llmModelName))
			throw new ArgumentException($"{llmModelName} is not one of the configured models. The following are the available, configured models: {string.Join(",", _chatClients.Keys)}", nameof(llmModelName));

		var client = _chatClients[llmModelName];
		var config = _modelConfigs[llmModelName];

		var openAiMessages = messages.Select(ToOpenAiMessage).ToList();

		requestOptions ??= LlmRequestOptions.Default;

		// The service settles the prompt's level against the levels ticked for this model,
		// so every caller gets the same answer without resolving it first.
		ReasoningLevel? level = requestOptions.ReasoningLevel is { } asked
			? ReasoningLevelResolver.Resolve(config, asked)
			: null;

		// The retry shape lives in TransientRetry.cs, shared with the other four remote
		// services, so the cold-start path (slow DNS/TLS/JIT warmup on the first call after
		// a reboot) gets a few escalating-timeout attempts instead of an unhandled failure.
		// A retry count of 0 still runs the body once.
		//
		// Each send counts its own attempts, so the retry after a fallback starts from 1
		// rather than inheriting the abandoned send's stretched timeout — the attempt lives
		// in Polly's per-execution context, not in the shared pipeline.
		async Task<ClientResult<ChatCompletion>> SendAsync(RequestShape shape)
		{
			ChatCompletionOptions options = BuildChatOptions(config, shape.FastMode, shape.Level, shape.SendsTemperature);

			// Heavier reasoning takes longer, so each try may wait a multiple of the user's
			// wait time; the multiple comes from the one limits table both services share.
			TimeSpan waitPerTry = ReasoningLimits.For(shape.Level).WaitPerTry(TimeSpan.FromSeconds(_timeoutSeconds));

			// Handing the token to the pipeline is what makes the ladder escapable, and it
			// is the whole mechanism: Polly checks it at the head of every attempt and
			// waits on it during the backoff, so an outside cancel ends the retry loop
			// there rather than being re-read as one more transient blip (issue #256).
			return await _retryPipeline.ExecuteWithAttemptAsync(async (attempt, overallToken) =>
			{
				using var thisTryCts = new CancellationTokenSource(waitPerTry * attempt, _timeProvider);
				using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(overallToken, thisTryCts.Token);
				return await client.CompleteChatAsync(openAiMessages, options, linkedCts.Token).ConfigureAwait(false);
			}, cancellationToken).ConfigureAwait(false);
		}

		var (result, sent) = await SendWithFallbacksAsync(
			SendAsync,
			config,
			new RequestShape(level, requestOptions.FastMode),
			requestOptions).ConfigureAwait(false);

		if (sent.FastMode)
			LogServedTier(result.Value);

		if (result.Value.Content.Count > 0)
		{
			return result.Value.Content[0].Text;
		}
		return string.Empty;
	}

	/// <summary>
	/// What a fallback can drop from a request: the reasoning level, with the model's
	/// temperature, and Fast mode. Everything else about the request stays the same on
	/// every send.
	/// </summary>
	/// <param name="Level">The resolved level to send, or null to send none.</param>
	/// <param name="FastMode">Whether to ask for Fast mode.</param>
	/// <param name="SendsTemperature">
	/// Whether the model's temperature may go on the request. False on every send after a
	/// reasoning fallback; see <see cref="SendWithFallbacksAsync"/>.
	/// </param>
	private readonly record struct RequestShape(ReasoningLevel? Level, bool FastMode, bool SendsTemperature = true);

	/// <summary>
	/// Sends the request and, if the reasoning level or the fast service tier is what the
	/// service refused, sends it again without that one setting rather than losing the
	/// user's text. Mirrors the Anthropic service. Each setting can be dropped once, so a
	/// request that asked for both is sent at most three times. Both fallbacks go in the
	/// cheaper direction (no reasoning, standard speed), so recovering without asking can
	/// never surprise anyone's bill. The user's saved settings are never changed: access
	/// may be granted, or the model changed, later.
	/// <para>
	/// The callbacks run only after a send succeeded, so the user is never told "it ran
	/// without a reasoning level" about a request that then failed outright.
	/// </para>
	/// <para>
	/// Unlike Anthropic, OpenAI Fast mode has no capacity pool or rate limit of its own, so
	/// a 429 is the account's ordinary limit: dropping the tier would not get the user
	/// served any sooner, and the retry ladder around each send already had a go at it.
	/// Only a rejected tier is handled here.
	/// </para>
	/// </summary>
	private static async Task<(ClientResult<ChatCompletion> Result, RequestShape Sent)> SendWithFallbacksAsync(
		Func<RequestShape, Task<ClientResult<ChatCompletion>>> send,
		LlmModelConfig config,
		RequestShape shape,
		LlmRequestOptions requestOptions)
	{
		FastModeFallback? fastModeFallback = null;
		ReasoningLevel? refusedLevel = null;

		while (true)
		{
			try
			{
				var result = await send(shape).ConfigureAwait(false);

				if (fastModeFallback is not null)
					requestOptions.OnFastModeFallback?.Invoke(fastModeFallback);
				if (refusedLevel is { } refused)
					requestOptions.OnReasoningFallback?.Invoke(refused);

				return (result, shape);
			}
			catch (ClientResultException ex)
			{
				bool reasoningCause = shape.Level is not null
					&& ReasoningFailure.IsCause(ex.Status, ex.Message);
				FastModeFallbackReason? fastModeCause = shape.FastMode
					? FastModeFailure.Classify(ex.Status, ex.Message)
					: null;

				// With both settings on, a message that names the service tier is taken at
				// its word even if it also names reasoning; otherwise the reasoning level is
				// the one dropped. A 403 that names neither is left to the Fast mode rule,
				// which reads it as the Fast mode entitlement gate.
				bool dropReasoning = reasoningCause
					&& !(fastModeCause is not null && FastModeFailure.MentionsFastMode(ex.Message));

				if (dropReasoning)
				{
					refusedLevel = shape.Level;
					ErrorLogger.LogInfo("LLM", $"{ReasoningMessages.Fallback(config.Name, shape.Level!.Value)} OpenAI said: {ex.Message}");
					// The temperature goes too. A reasoning model that refused one level still
					// reasons at its default with none, and then refuses a temperature, so the
					// retry would fail on an error that names no reasoning term and the user's
					// text would be lost after all (issue #438).
					shape = shape with { Level = null, SendsTemperature = false };
				}
				else if (fastModeCause is { } reason)
				{
					fastModeFallback = new FastModeFallback(reason, ex.Message);
					ErrorLogger.LogInfo("LLM", FastModeMessages.DescribeForLog(fastModeFallback));
					shape = shape with { FastMode = false };
				}
				else
				{
					throw; // Neither setting caused it; report it as it is.
				}
			}
		}
	}

	/// <summary>
	/// The single place chat request options are constructed.
	/// </summary>
	/// <param name="fastMode">
	/// Premium inference speed on the same model, at roughly twice the token price.
	/// Requested via the service tier; nothing is set when off, so the account default
	/// applies exactly as before.
	/// </param>
	/// <param name="level">
	/// The resolved reasoning level, or null to send no reasoning effort, so the request is
	/// the same as before reasoning levels existed.
	/// </param>
	/// <param name="allowTemperature">
	/// False to leave the model's temperature off whatever the level, as the retry after a
	/// reasoning fallback does.
	/// </param>
	internal static ChatCompletionOptions BuildChatOptions(
		LlmModelConfig config,
		bool fastMode = false,
		ReasoningLevel? level = null,
		bool allowTemperature = true)
	{
		var options = new ChatCompletionOptions();
		// OpenAI refuses a custom temperature while the model reasons.
		if (allowTemperature && config.CustomTemperature.HasValue && OpenAiReasoningFields.SendsTemperature(level))
		{
			options.Temperature = (float)config.CustomTemperature.Value;
		}
		if (OpenAiReasoningFields.Effort(level) is { } effort)
		{
#pragma warning disable OPENAI001 // Evaluation-only SDK surface; see OpenAiReasoningFields.
			options.ReasoningEffortLevel = effort;
#pragma warning restore OPENAI001
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
