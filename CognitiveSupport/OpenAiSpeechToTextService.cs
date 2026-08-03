using CognitiveSupport.Extensions;
using OpenAI.Audio;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Timeout;

namespace CognitiveSupport;

public class OpenAiSpeechToTextService : ISpeechToTextService
{
	public string ServiceName { get; init; }

	private readonly AudioClient _audioClient;
	private readonly int _timeoutSeconds;

	public OpenAiSpeechToTextService(
		string serviceName,
		AudioClient audioClient,
		int timeoutSeconds)
	{
		this.ServiceName = serviceName;
		_audioClient = audioClient ?? throw new ArgumentNullException(nameof(audioClient));
		_timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 10;
	}

	public async Task<string> ConvertAudioToText(
		string speechToTextPrompt,
		string audioffilePath,
		CancellationToken overallCancellationToken,
		int? timeoutSeconds = null)
	{
		if (string.IsNullOrEmpty(audioffilePath))
			throw new ArgumentException($"'{nameof(audioffilePath)}' cannot be null or empty.", nameof(audioffilePath));

		const string AttemptKey = "Attempt";

		var delay = Backoff.LinearBackoff(TimeSpan.FromMilliseconds(500), retryCount: 3, factor: 1);
		var retryPolicy = Policy
			.Handle<HttpRequestException>()
			.Or<TimeoutRejectedException>()
			.Or<TaskCanceledException>()
				.WaitAndRetryAsync(
					delay,
					onRetry: (exception, timeSpan, attemptNumber, context) =>
					{
						int attempt = context.ContainsKey(AttemptKey) ? (int)context[AttemptKey] : 1;
						context[AttemptKey] = ++attempt;
					}
				);

		var context = new Context();
		context[AttemptKey] = 1;

		var response = await retryPolicy.ExecuteAsync(async (context, overallToken) =>
		{
			int attempt = context.ContainsKey(AttemptKey) ? (int)context[AttemptKey] : 1;
			int baseTimeout = timeoutSeconds ?? _timeoutSeconds;
			int timeout = baseTimeout * attempt;
			using var thisTryCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(overallToken, thisTryCts.Token);

			// Beep swallows the first, non-retry attempt itself; retries beep once per attempt.
			this.Beep(attempt);

			return await TranscribeViaWhisper(speechToTextPrompt, audioffilePath, linkedCts.Token).ConfigureAwait(false);
		}, context, overallCancellationToken).ConfigureAwait(false);

		return response;



	}

	private async Task<string> TranscribeViaWhisper(
		string speechToTextPrompt,
		string audioFilePath,
		CancellationToken cancellationToken)
	{
		AudioTranscriptionOptions options = new()
		{
			Prompt = speechToTextPrompt,
		};

		using var stream = File.OpenRead(audioFilePath);
		var result = await _audioClient.TranscribeAudioAsync(stream, Path.GetFileName(audioFilePath), options, cancellationToken);
		return result.Value.Text;
	}
}