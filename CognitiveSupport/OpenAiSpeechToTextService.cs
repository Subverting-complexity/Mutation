using CognitiveSupport.Extensions;
using OpenAI.Audio;
using Polly;
using System.ClientModel;

namespace CognitiveSupport;

public class OpenAiSpeechToTextService : ISpeechToTextService
{
	public string ServiceName { get; init; }

	// Holds no call state, so one pipeline serves every transcription, concurrent or not.
	// Built per instance rather than shared statically only so the clock behind it can be
	// replaced; the app makes one of these, so that costs nothing.
	private readonly ResiliencePipeline _retryPipeline;

	private readonly AudioClient _audioClient;
	private readonly int _timeoutSeconds;
	private readonly TimeProvider _timeProvider;
	private readonly Action<int> _announceAttempt;

	/// <param name="timeProvider">
	/// Test seam only; null in production. Drives both the retry backoff and each
	/// attempt's deadline, so a test can read back the waits that were armed instead of
	/// sitting through them.
	/// </param>
	/// <param name="announceAttempt">
	/// Test seam only; null in production, where it beeps. Substituting it is what lets a
	/// test assert the attempt number the listener is told about — and, just as usefully,
	/// keeps a suite that drives real retries from playing sounds at whoever ran it.
	/// </param>
	public OpenAiSpeechToTextService(
		string serviceName,
		AudioClient audioClient,
		int timeoutSeconds,
		TimeProvider? timeProvider = null,
		Action<int>? announceAttempt = null)
	{
		this.ServiceName = serviceName;
		_audioClient = audioClient ?? throw new ArgumentNullException(nameof(audioClient));
		_timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 10;
		_timeProvider = timeProvider ?? TimeProvider.System;
		_announceAttempt = announceAttempt ?? (attempt => this.Beep(attempt));

		// ClientResultException is the only failure the OpenAI SDK ever reports: it wraps
		// a dropped connection (Status 0) as readily as a 429 or a 503, so a pipeline that
		// handled only HttpRequestException — as this one did until issue #311 put a test
		// on it — retried nothing but its own per-attempt timeout. LlmService already
		// classified it this way; this is the same call, on the same statuses, so a bad key
		// (401) still fails fast.
		_retryPipeline = TransientRetry.Pipeline(
			retryCount: 3,
			TransientRetry.Transient()
				.Handle<ClientResultException>(ex => LlmHttpStatus.IsTransient(ex.Status)),
			timeProvider);
	}

	public async Task<string> ConvertAudioToText(
		string speechToTextPrompt,
		string audioffilePath,
		CancellationToken overallCancellationToken,
		int? timeoutSeconds = null)
	{
		if (string.IsNullOrEmpty(audioffilePath))
			throw new ArgumentException($"'{nameof(audioffilePath)}' cannot be null or empty.", nameof(audioffilePath));

		var response = await _retryPipeline.ExecuteWithAttemptAsync(async (attempt, overallToken) =>
		{
			int baseTimeout = timeoutSeconds ?? _timeoutSeconds;
			int timeout = baseTimeout * attempt;
			using var thisTryCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout), _timeProvider);
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(overallToken, thisTryCts.Token);

			// The default announcer swallows the first, non-retry attempt itself and beeps once
			// per attempt after that; every attempt is reported here regardless.
			_announceAttempt(attempt);

			return await TranscribeViaWhisper(speechToTextPrompt, audioffilePath, linkedCts.Token).ConfigureAwait(false);
		}, overallCancellationToken).ConfigureAwait(false);

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