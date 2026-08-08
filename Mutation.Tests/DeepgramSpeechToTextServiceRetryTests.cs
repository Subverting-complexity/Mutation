using CognitiveSupport;
using Deepgram.Clients.Interfaces.v1;
using Deepgram.Models.Listen.v1.REST;

namespace Mutation.Tests;

/// <summary>
/// Issue #311: <c>TranscriptionCancellationTests</c> looked like it covered this, but the
/// thing it drives is a hand-written stand-in that imitates the retry loop. These drive
/// the real service, so what is asserted is the loop that actually ships.
/// </summary>
public class DeepgramSpeechToTextServiceRetryTests
{
	private const int BaseTimeoutSeconds = 10;

	private static string WriteAudioFile()
	{
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
		File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
		return path;
	}

	private static (DeepgramSpeechToTextService Service, FakeListenClient Client, RecordingClock Clock, List<int> Announced)
		CreateService(params Func<int, SyncResponse>[] replies)
	{
		var client = new FakeListenClient(replies);
		var clock = new RecordingClock();
		var announced = new List<int>();
		var service = new DeepgramSpeechToTextService(
			"Deepgram",
			client,
			"nova-3",
			BaseTimeoutSeconds,
			clock,
			announced.Add);
		return (service, client, clock, announced);
	}

	private static SyncResponse Transcript(string text) =>
		new()
		{
			Results = new Results
			{
				Channels =
				[
					new Channel { Alternatives = [new Alternative { Transcript = text }] }
				]
			}
		};

	[Fact]
	public async Task ADroppedConnectionIsRetriedUntilItSucceeds()
	{
		// A dropped connection on the way to Deepgram is the case the retry ladder exists
		// for: the recording is already made, and losing it to one bad packet would be
		// the worst possible outcome. Deepgram's client does not catch HttpRequestException,
		// so this is the exception the service really faces here.
		var (service, client, _, _) = CreateService(
			_ => throw new HttpRequestException("connection reset"),
			_ => throw new HttpRequestException("connection reset"),
			_ => Transcript("the recovered transcript"));

		string path = WriteAudioFile();
		try
		{
			string text = await service.ConvertAudioToText("", path, CancellationToken.None);

			Assert.Equal("the recovered transcript", text);
			Assert.Equal(3, client.Calls);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public async Task ADroppedConnectionGivesUpAfterThreeRetries()
	{
		var (service, client, _, _) = CreateService(
			_ => throw new HttpRequestException("connection reset"));

		string path = WriteAudioFile();
		try
		{
			await Assert.ThrowsAsync<HttpRequestException>(() =>
				service.ConvertAudioToText("", path, CancellationToken.None));

			// The first try plus three retries, and then it reports the failure rather
			// than retrying for ever.
			Assert.Equal(4, client.Calls);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public async Task AFailureDeepgramItselfReportsIsNotRetried()
	{
		// A rejected API key does not become valid on the second ask, so reporting it at
		// once beats making the user wait out the whole ladder first.
		//
		// What actually decides it, though, is not the status — it is whether the error
		// carried a body. Deepgram's client deserializes a JSON error into this exception,
		// which nothing here handles; an error with an empty body falls through to
		// EnsureSuccessStatusCode instead and arrives as the HttpRequestException the test
		// above retries. So the real rule today is "an error with a body is final, one
		// without gets four attempts", regardless of whether it was a 429 or a 401. That is
		// stranger than any rule anyone chose, and DeepgramRESTException carries no status
		// to fix it with. Recorded rather than papered over — see issue #316.
		var (service, client, _, _) = CreateService(
			_ => throw new Deepgram.Models.Exceptions.v1.DeepgramRESTException("401 Unauthorized"));

		string path = WriteAudioFile();
		try
		{
			await Assert.ThrowsAsync<Deepgram.Models.Exceptions.v1.DeepgramRESTException>(() =>
				service.ConvertAudioToText("", path, CancellationToken.None));

			Assert.Equal(1, client.Calls);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public async Task EachAttemptGivesItselfLongerThanTheLast()
	{
		// The point of the ladder is that a slow cold start gets more room each time, not
		// the same too-short window four times over.
		var (service, _, clock, _) = CreateService(
			_ => throw new HttpRequestException("connection reset"));

		string path = WriteAudioFile();
		try
		{
			await Assert.ThrowsAsync<HttpRequestException>(() =>
				service.ConvertAudioToText("", path, CancellationToken.None));

			Assert.Equal([10d, 20d, 30d, 40d], clock.DeadlineSeconds);
			Assert.Equal([500d, 1000d, 1500d], clock.BackoffMilliseconds);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public async Task ACallerSuppliedTimeoutSetsTheLadderInsteadOfTheConfiguredOne()
	{
		var (service, _, clock, _) = CreateService(
			_ => throw new HttpRequestException("connection reset"));

		string path = WriteAudioFile();
		try
		{
			await Assert.ThrowsAsync<HttpRequestException>(() =>
				service.ConvertAudioToText("", path, CancellationToken.None, timeoutSeconds: 15));

			Assert.Equal([15d, 30d, 45d, 60d], clock.DeadlineSeconds);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public async Task TheListenerIsToldWhichRetryThisIs()
	{
		// One beep per attempt, and none for the first: a first try has nothing to report
		// yet, and a third retry has to be distinguishable from a first (issue #216).
		var (service, _, _, announced) = CreateService(
			_ => throw new HttpRequestException("connection reset"));

		string path = WriteAudioFile();
		try
		{
			await Assert.ThrowsAsync<HttpRequestException>(() =>
				service.ConvertAudioToText("", path, CancellationToken.None));

			Assert.Equal([1, 2, 3, 4], announced);
			Assert.Equal([2, 3, 4], announced.Where(RetryBeepPolicy.ShouldBeep));
		}
		finally
		{
			File.Delete(path);
		}
	}

	/// <summary>
	/// Answers each call from a script, so a test says what the network does on attempt
	/// one, two and three. The last entry stands in for every call past the end of the
	/// script, which is what lets "always fails" be written as a single entry.
	/// </summary>
	private sealed class FakeListenClient(Func<int, SyncResponse>[] replies) : IListenRESTClient
	{
		internal int Calls { get; private set; }

		public Task<SyncResponse> TranscribeFile(
			byte[] source,
			PreRecordedSchema? prerecordedSchema,
			CancellationTokenSource? cancellationToken = null,
			Dictionary<string, string>? addons = null,
			Dictionary<string, string>? headers = null)
		{
			int call = ++Calls;
			return Task.FromResult(replies[Math.Min(call, replies.Length) - 1](call));
		}

		public Task<SyncResponse> TranscribeFile(
			Stream source,
			PreRecordedSchema? prerecordedSchema,
			CancellationTokenSource? cancellationToken = null,
			Dictionary<string, string>? addons = null,
			Dictionary<string, string>? headers = null) =>
			throw new NotSupportedException("The service uploads bytes, not a stream.");

		public Task<SyncResponse> TranscribeUrl(
			UrlSource source,
			PreRecordedSchema? prerecordedSchema,
			CancellationTokenSource? cancellationToken = null,
			Dictionary<string, string>? addons = null,
			Dictionary<string, string>? headers = null) =>
			throw new NotSupportedException("The service uploads a local recording.");

		public Task<AsyncResponse> TranscribeFileCallBack(
			byte[] source,
			string? callBack,
			PreRecordedSchema? prerecordedSchema,
			CancellationTokenSource? cancellationToken = null,
			Dictionary<string, string>? addons = null,
			Dictionary<string, string>? headers = null) =>
			throw new NotSupportedException("The service transcribes synchronously.");

		public Task<AsyncResponse> TranscribeFileCallBack(
			Stream source,
			string? callBack,
			PreRecordedSchema? prerecordedSchema,
			CancellationTokenSource? cancellationToken = null,
			Dictionary<string, string>? addons = null,
			Dictionary<string, string>? headers = null) =>
			throw new NotSupportedException("The service transcribes synchronously.");

		public Task<AsyncResponse> TranscribeUrlCallBack(
			UrlSource source,
			string? callBack,
			PreRecordedSchema? prerecordedSchema,
			CancellationTokenSource? cancellationToken = null,
			Dictionary<string, string>? addons = null,
			Dictionary<string, string>? headers = null) =>
			throw new NotSupportedException("The service transcribes synchronously.");
	}
}
