using CognitiveSupport.Extensions;
using Deepgram.Models.Listen.v1.REST;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Timeout;
using System.Text.RegularExpressions;

namespace CognitiveSupport;

public class DeepgramSpeechToTextService : ISpeechToTextService
{
	public string ServiceName { get; init; }

	private readonly string _modelId;
	private readonly Deepgram.Clients.Interfaces.v1.IListenRESTClient _deepgramClient;
	private readonly int _timeoutSeconds;

	public DeepgramSpeechToTextService(
		string serviceName,
		Deepgram.Clients.Interfaces.v1.IListenRESTClient deepgramClient,
		string modelId,
		int timeoutSeconds = 10)
	{
		ServiceName = serviceName;
		_deepgramClient = deepgramClient ?? throw new ArgumentNullException(nameof(deepgramClient));
		_modelId = modelId ?? throw new ArgumentNullException(nameof(modelId), "Check your Deepgram API documentation for supported modelIds.");
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

		List<string> keyterms = ParseKeyterms(speechToTextPrompt);

		var audioBytes = await File.ReadAllBytesAsync(audioffilePath, overallCancellationToken).ConfigureAwait(false);
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
			// Linear backoff for timeout duration, but respect the requested timeout as a minimum for the first attempt.
			// Removing the 60s cap to allow for longer file transcriptions.
			int timeout = baseTimeout * attempt;
			using var thisTryCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(overallToken, thisTryCts.Token);

			// Beep swallows the first, non-retry attempt itself; retries beep once per attempt.
			this.Beep(attempt);

			return await TranscribeViaDeepgram(keyterms, audioBytes, linkedCts).ConfigureAwait(false);
		}, context, overallCancellationToken).ConfigureAwait(false);
	
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
