using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CognitiveSupport;
using Mutation.Ui;

namespace Mutation.Tests;

// Covers issue #178: a failed recorder start must not leave the app reporting
// "recording" with no active capture, and a second start while a recorder is
// already live must be rejected rather than orphaning the running capture.
public class SpeechToTextManagerStartGuardTests : IDisposable
{
	private readonly string _tempDirectory;
	private readonly Settings _settings;

	public SpeechToTextManagerStartGuardTests()
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
	public async Task StartRecording_WhenRecorderStartFails_LeavesManagerIdleWithNoOrphanSession()
	{
		var failing = new FakeAudioRecorder(startError: new InvalidOperationException("capture device unavailable"));
		using var manager = new SpeechToTextManager(_settings, () => failing);

		await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartRecordingAsync(0));

		Assert.False(manager.Recording);
		Assert.Null(manager.CurrentRecordingSession);
		Assert.True(failing.Disposed);
		// The empty session file created before the start must be cleaned up.
		Assert.Empty(manager.GetSessions());
	}

	[Fact]
	public async Task StartRecording_AfterFailedStart_CanStartAgain()
	{
		var recorders = new List<FakeAudioRecorder>();
		using var manager = new SpeechToTextManager(_settings, () =>
		{
			// First recorder fails its start, the second succeeds.
			var recorder = new FakeAudioRecorder(
				startError: recorders.Count == 0 ? new InvalidOperationException("first start fails") : null);
			recorders.Add(recorder);
			return recorder;
		});

		await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartRecordingAsync(0));
		Assert.False(manager.Recording);

		var session = await manager.StartRecordingAsync(0);

		Assert.True(manager.Recording);
		Assert.Equal(session.FilePath, manager.CurrentRecordingSession?.FilePath);
		Assert.Equal(2, recorders.Count);
		Assert.True(recorders[1].Started);
	}

	[Fact]
	public async Task StartRecording_WhileAlreadyRecording_ThrowsAndLeavesLiveRecorderUntouched()
	{
		var recorders = new List<FakeAudioRecorder>();
		using var manager = new SpeechToTextManager(_settings, () =>
		{
			var recorder = new FakeAudioRecorder();
			recorders.Add(recorder);
			return recorder;
		});

		var session = await manager.StartRecordingAsync(0);
		Assert.True(manager.Recording);
		var live = Assert.Single(recorders);

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartRecordingAsync(0));

		Assert.Contains("already in progress", ex.Message, StringComparison.OrdinalIgnoreCase);
		// The guard runs before a new recorder is constructed, so the live capture
		// is neither overwritten nor disposed.
		Assert.Single(recorders);
		Assert.False(live.Disposed);
		Assert.True(manager.Recording);
		Assert.Equal(session.FilePath, manager.CurrentRecordingSession?.FilePath);
	}

	private sealed class FakeAudioRecorder : IAudioRecorder
	{
		private readonly Exception? _startError;

		public FakeAudioRecorder(Exception? startError = null)
		{
			_startError = startError;
		}

		public bool Started { get; private set; }
		public bool Disposed { get; private set; }

		public double? TrimmedSpeechSeconds => null;
		public Exception? CaptureException => null;

		public void StartRecording(int captureDeviceIndex, string outputFile, SilenceTrimmerOptions? silenceOptions = null)
		{
			if (_startError != null)
				throw _startError;

			Started = true;
		}

		public void StopRecording()
		{
		}

		public void Dispose()
		{
			Disposed = true;
		}
	}
}
