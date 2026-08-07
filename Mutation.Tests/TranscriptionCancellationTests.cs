using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;
using Mutation.Ui;

namespace Mutation.Tests;

// Covers issue #179: cancelling a transcription must not dispose the
// CancellationTokenSource while background retries still link fresh tokens to
// it. Disposal there surfaced an ObjectDisposedException as a raw error dialog
// instead of a clean "Transcription cancelled".
public class TranscriptionCancellationTests : IDisposable
{
	private readonly string _tempDirectory;
	private readonly Settings _settings;

	public TranscriptionCancellationTests()
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
	public void CancelTranscription_SignalsCancellation_WithoutDisposingSource()
	{
		using var state = new SpeechToTextState(() => null);
		var token = state.StartTranscription();

		state.CancelTranscription();

		Assert.True(token.IsCancellationRequested);

		// A retry attempt links a fresh source to the token on every try. With the
		// source only cancelled (not disposed) this must succeed and yield an
		// already-cancelled token, never throw ObjectDisposedException.
		using var thisTry = new CancellationTokenSource();
		var ex = Record.Exception(() =>
		{
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, thisTry.Token);
			Assert.True(linked.IsCancellationRequested);
		});
		Assert.Null(ex);

		// The transcription is still tearing down, so it remains reported as active
		// until the owning task ends it.
		Assert.True(state.TranscribingAudio);
	}

	[Fact]
	public void EndTranscription_DisposesSource_AndClearsTranscribingFlag()
	{
		using var state = new SpeechToTextState(() => null);
		state.StartTranscription();
		Assert.True(state.TranscribingAudio);

		state.EndTranscription();
		Assert.False(state.TranscribingAudio);

		// Idempotent: the owning task's finally and a later Dispose can both run.
		state.EndTranscription();
		Assert.False(state.TranscribingAudio);
	}

	[Fact]
	public void StartTranscription_WhenPriorSourceLingers_CancelsPriorToken()
	{
		using var state = new SpeechToTextState(() => null);
		var first = state.StartTranscription();
		Assert.False(first.IsCancellationRequested);

		// A second start without an intervening End must cancel the stale source
		// before disposing it rather than abandoning an operation still tied to it.
		var second = state.StartTranscription();

		Assert.True(first.IsCancellationRequested);
		Assert.False(second.IsCancellationRequested);
	}

	[Fact]
	public async Task CancelTranscription_WhileServiceRetries_SurfacesOperationCanceled_NotObjectDisposed()
	{
		string sessionPath = Path.Combine(_tempDirectory, "session_2024-01-01_00-00-00.ogg");
		await File.WriteAllTextAsync(sessionPath, "audio");
		var session = new SpeechSession(sessionPath, new DateTime(2024, 1, 1));

		var service = new RetryingCancellableService();
		using var manager = new SpeechToTextManager(_settings, () => new NoopAudioRecorder());

		var transcription = manager.TranscribeExistingRecordingAsync(service, session, "prompt", CancellationToken.None);

		// Wait until the service is mid-call, then cancel while it is about to link
		// a new token to the caller's source — the exact race the bug hit.
		await service.Started;
		manager.CancelTranscription();
		service.Proceed();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transcription);
	}

	// Mimics OpenAiSpeechToTextService's retry loop: it links a fresh source to
	// the caller's token on each attempt. The test drives it to that point after
	// a cancel to reproduce the dispose-during-retry race.
	private sealed class RetryingCancellableService : ISpeechToTextService
	{
		private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public string ServiceName { get; init; } = "fake";

		public Task Started => _started.Task;
		public void Proceed() => _proceed.TrySetResult();

		public async Task<string> ConvertAudioToText(string speechToTextPrompt, string audioffilePath, CancellationToken overallCancellationToken, int? timeoutSeconds = null)
		{
			_started.TrySetResult();
			await _proceed.Task.ConfigureAwait(false);

			using var thisTry = new CancellationTokenSource();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(overallCancellationToken, thisTry.Token);
			linked.Token.ThrowIfCancellationRequested();
			return "unexpected-success";
		}
	}

	private sealed class NoopAudioRecorder : IAudioRecorder
	{
		public double? TrimmedSpeechSeconds => null;
		public IReadOnlyList<SilenceRemovalPoint> RemovedSilences => Array.Empty<SilenceRemovalPoint>();
		public Exception? CaptureException => null;

		public void StartRecording(int captureDeviceIndex, string outputFile, SilenceTrimmerOptions? silenceOptions = null)
		{
		}

		public void StopRecording()
		{
		}

		public void Dispose()
		{
		}
	}
}
