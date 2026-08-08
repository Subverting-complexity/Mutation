using CognitiveSupport.Extensions;
using Deepgram.Models.Listen.v1.REST;
using Polly;
using System.Text.RegularExpressions;

namespace CognitiveSupport;

public class DeepgramSpeechToTextService : ISpeechToTextService
{
	public string ServiceName { get; init; }

	// Holds no call state, so one pipeline serves every transcription, concurrent or not.
	// Built per instance rather than shared statically only so the clock behind it can be
	// replaced; the app makes one of these, so that costs nothing.
	private readonly ResiliencePipeline _retryPipeline;

	private readonly string _modelId;
	private readonly Deepgram.Clients.Interfaces.v1.IListenRESTClient _deepgramClient;
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
	public DeepgramSpeechToTextService(
		string serviceName,
		Deepgram.Clients.Interfaces.v1.IListenRESTClient deepgramClient,
		string modelId,
		int timeoutSeconds = 10,
		TimeProvider? timeProvider = null,
		Action<int>? announceAttempt = null)
	{
		ServiceName = serviceName;
		_deepgramClient = deepgramClient ?? throw new ArgumentNullException(nameof(deepgramClient));
		_modelId = modelId ?? throw new ArgumentNullException(nameof(modelId), "Check your Deepgram API documentation for supported modelIds.");
		_timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 10;
		_timeProvider = timeProvider ?? TimeProvider.System;
		_announceAttempt = announceAttempt ?? (attempt => this.Beep(attempt));
		_retryPipeline = TransientRetry.Pipeline(
			retryCount: 3,
			TransientRetry.Transient()
				.Handle<Deepgram.Models.Exceptions.v1.DeepgramException>(DeepgramFailure.IsWorthAnotherAttempt),
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

		List<string> keyterms = ParseKeyterms(speechToTextPrompt);

		var audioBytes = await File.ReadAllBytesAsync(audioffilePath, overallCancellationToken).ConfigureAwait(false);

		var response = await _retryPipeline.ExecuteWithAttemptAsync(async (attempt, overallToken) =>
		{
			int baseTimeout = timeoutSeconds ?? _timeoutSeconds;
			// Linear backoff for timeout duration, but respect the requested timeout as a minimum for the first attempt.
			// Removing the 60s cap to allow for longer file transcriptions.
			int timeout = baseTimeout * attempt;
			using var thisTryCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout), _timeProvider);
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(overallToken, thisTryCts.Token);

			// The default announcer swallows the first, non-retry attempt itself and beeps once
			// per attempt after that; every attempt is reported here regardless.
			_announceAttempt(attempt);

			return await TranscribeViaDeepgram(keyterms, audioBytes, linkedCts).ConfigureAwait(false);
		}, overallCancellationToken).ConfigureAwait(false);

		return response?.Results?.Channels?.FirstOrDefault()?.Alternatives?.FirstOrDefault()?.Transcript
			?? "(no transcript available)";



	}

	private async Task<SyncResponse> TranscribeViaDeepgram(
		List<string> keyterms,
		byte[] audioBytes,
		CancellationTokenSource cancellationTokenSource)
	{
		var response = await _deepgramClient.TranscribeFile(
		  audioBytes,
		  new PreRecordedSchema()
		  {
			  Model = _modelId,
			  Keyterm = keyterms,

			  Punctuate = true,
			  FillerWords = false,
			  Measurements = true,
			  SmartFormat = true,
		  }, cancellationTokenSource);
		return response;
	}

	/// <summary>
	/// Reads the comma-separated keyterms out of a prompt line beginning <c>keyterms:</c>.
	/// <para>
	/// The list runs to the end of its line, with one optional full stop allowed to close it.
	/// It used to stop at the first period anywhere, which meant "keyterms: Dr. Bosch, Mutation."
	/// yielded the single term "Dr" — and keyterms exist precisely to help with names and jargon,
	/// the text most likely to contain an abbreviation period. A prompt with no closing period
	/// dropped every term instead (issue #245).
	/// </para>
	/// </summary>
	internal static List<string> ParseKeyterms(string speechToTextPrompt)
	{
		if (speechToTextPrompt == null)
			speechToTextPrompt = string.Empty;

		// Multiline `$` matches before the `\n` of a CRLF, so the `\r` has to be spelled out
		// or the lazy match swallows it — and with it the closing period it was meant to trim.
		string pattern = @"(?<=\bkeyterms:[ \t]*).*?(?=\.?[ \t]*\r?$)";
		Match match = Regex.Match(speechToTextPrompt, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
		if (match.Success)
		{
			string keytermsString = match.Value.Trim();
			var keyterms = keytermsString.Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
			.ToList();
			return keyterms;
		}
		else
			return new List<string>();
	}
}
