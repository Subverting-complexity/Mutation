using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;
using Mutation.Ui;

namespace Mutation.Tests;

// Covers issue #268: the end-of-recording beep only ever sounded as a side effect of
// the transcription retry signal, so silencing the first (non-retry) attempt in #216
// silenced it outright. Stopping a recording now raises its own notification — the
// hook AudioSessionManager plays BeepType.End from — and it has to land after the
// recorder is closed (or the beep is captured into the recording it ends) and before
// the transcription request (or the user waits out a network call to hear that
// capture stopped).
public class EndOfRecordingNotificationTests : IDisposable
{
	private readonly string _tempDirectory;
	private readonly Settings _settings;

	public EndOfRecordingNotificationTests()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), "MutationTests_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDirectory);
		_settings = new Settings
		{
			SpeechToTextSettings = new SpeechToTextSettings
			{
				TempDirectory = _tempDirectory
			}
		};
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(_tempDirectory, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	[Fact]
	public async Task StopRecordingAndTranscribe_NotifiesAfterTheRecorderCloses_AndBeforeTranscribing()
	{
		var events = new List<string>();
		var recorder = new RecordingAudioRecorder(events);
		using var manager = new SpeechToTextManager(_settings, () => recorder);
		await manager.StartRecordingAsync(0);

		await manager.StopRecordingAndTranscribeAsync(
			new RecordingService(events),
			"prompt",
			CancellationToken.None,
			onRecordingStopped: () => events.Add("notify"));

		Assert.Equal(new[] { "stop", "dispose", "notify", "transcribe" }, events);
	}

	[Fact]
	public async Task StopRecordingAndTranscribe_NotifiesExactlyOnce()
	{
		int notifications = 0;
		using var manager = new SpeechToTextManager(_settings, () => new StubAudioRecorder());
		await manager.StartRecordingAsync(0);

		await manager.StopRecordingAndTranscribeAsync(
			new StubService(),
			"prompt",
			CancellationToken.None,
			onRecordingStopped: () => notifications++);

		Assert.Equal(1, notifications);
	}

	// Capture failed, so there is nothing to transcribe — but the microphone did stop,
	// and the user still needs to hear that their recording ended.
	[Fact]
	public async Task StopRecordingAndTranscribe_WhenCaptureFailed_StillNotifies()
	{
		bool notified = false;
		var recorder = new StubAudioRecorder { CaptureError = new InvalidOperationException("device lost") };
		using var manager = new SpeechToTextManager(_settings, () => recorder);
		await manager.StartRecordingAsync(0);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			manager.StopRecordingAndTranscribeAsync(
				new StubService(),
				"prompt",
				CancellationToken.None,
				onRecordingStopped: () => notified = true));

		Assert.True(notified);
	}

	[Fact]
	public async Task StopRecordingAndTranscribe_WhenNoSpeechDetected_StillNotifies()
	{
		bool notified = false;
		var recorder = new StubAudioRecorder { TrimmedSeconds = 0.01 };
		using var manager = new SpeechToTextManager(_settings, () => recorder);
		await manager.StartRecordingAsync(0);

		await Assert.ThrowsAsync<NoSpeechDetectedException>(() =>
			manager.StopRecordingAndTranscribeAsync(
				new StubService(),
				"prompt",
				CancellationToken.None,
				onRecordingStopped: () => notified = true));

		Assert.True(notified);
	}

	// A beep is a courtesy; it must never take down the transcription it reports on.
	[Fact]
	public async Task StopRecordingAndTranscribe_WhenNotificationThrows_TranscriptionStillSucceeds()
	{
		using var manager = new SpeechToTextManager(_settings, () => new StubAudioRecorder());
		await manager.StartRecordingAsync(0);

		string text = await manager.StopRecordingAndTranscribeAsync(
			new StubService(),
			"prompt",
			CancellationToken.None,
			onRecordingStopped: () => throw new InvalidOperationException("no audio device"));

		Assert.Equal(StubService.Transcript, text);
	}

	// Cancelling while the recorder is closing still ends the recording, so the signal
	// has to precede the cancellation check rather than be thrown past.
	[Fact]
	public async Task StopRecordingAndTranscribe_WhenCancelledDuringTheStop_StillNotifies()
	{
		bool notified = false;
		using var cts = new CancellationTokenSource();
		using var manager = new SpeechToTextManager(_settings, () => new StubAudioRecorder { OnStop = cts.Cancel });
		await manager.StartRecordingAsync(0);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			manager.StopRecordingAndTranscribeAsync(
				new StubService(),
				"prompt",
				cts.Token,
				onRecordingStopped: () => notified = true));

		Assert.True(notified);
	}

	private sealed class RecordingAudioRecorder : IAudioRecorder
	{
		private readonly List<string> _events;

		public RecordingAudioRecorder(List<string> events) => _events = events;

		public double? TrimmedSpeechSeconds => null;
		public Exception? CaptureException => null;

		public void StartRecording(int captureDeviceIndex, string outputFile, SilenceTrimmerOptions? silenceOptions = null)
		{
		}

		public void StopRecording() => _events.Add("stop");

		public void Dispose() => _events.Add("dispose");
	}

	private sealed class RecordingService : ISpeechToTextService
	{
		private readonly List<string> _events;

		public RecordingService(List<string> events) => _events = events;

		public string ServiceName { get; init; } = "fake";

		public Task<string> ConvertAudioToText(string speechToTextPrompt, string audioffilePath, CancellationToken overallCancellationToken, int? timeoutSeconds = null)
		{
			_events.Add("transcribe");
			return Task.FromResult("transcript");
		}
	}

	private sealed class StubAudioRecorder : IAudioRecorder
	{
		public double? TrimmedSeconds { get; init; }
		public Exception? CaptureError { get; init; }
		public Action? OnStop { get; init; }

		public double? TrimmedSpeechSeconds => TrimmedSeconds;
		public Exception? CaptureException => CaptureError;

		public void StartRecording(int captureDeviceIndex, string outputFile, SilenceTrimmerOptions? silenceOptions = null)
		{
		}

		public void StopRecording() => OnStop?.Invoke();

		public void Dispose()
		{
		}
	}

	private sealed class StubService : ISpeechToTextService
	{
		public const string Transcript = "transcript";

		public string ServiceName { get; init; } = "fake";

		public Task<string> ConvertAudioToText(string speechToTextPrompt, string audioffilePath, CancellationToken overallCancellationToken, int? timeoutSeconds = null)
			=> Task.FromResult(Transcript);
	}
}
