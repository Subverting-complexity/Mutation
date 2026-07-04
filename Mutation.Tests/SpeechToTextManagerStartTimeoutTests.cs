using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;
using Mutation.Ui;

namespace Mutation.Tests;

// Covers issue #177: starting a recording while a previous transcription still
// holds the recorder lock must fail fast with RecorderBusyException instead of
// waiting indefinitely, and a busy timeout must not leave an orphaned empty
// session file behind.
public class SpeechToTextManagerStartTimeoutTests : IDisposable
{
	private readonly string _tempDirectory;
	private readonly Settings _settings;
	private readonly SpeechToTextManager _manager;

	public SpeechToTextManagerStartTimeoutTests()
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
		_manager = new SpeechToTextManager(_settings);
	}

	public void Dispose()
	{
		_manager.Dispose();
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

	private string CreateSessionFile()
	{
		string sessionsDirectory = Path.Combine(_tempDirectory, Constants.SessionsDirectoryName);
		Directory.CreateDirectory(sessionsDirectory);
		string path = Path.Combine(sessionsDirectory, "session_2026-01-01_00-00-00.ogg");
		File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
		return path;
	}

	[Fact]
	public async Task StartRecording_WhileTranscriptionHoldsLock_ThrowsRecorderBusy()
	{
		string sessionPath = CreateSessionFile();
		var session = _manager.GetSessions().Single();

		using var serviceEntered = new SemaphoreSlim(0);
		using var releaseService = new SemaphoreSlim(0);
		var service = new BlockingSpeechToTextService(serviceEntered, releaseService);

		Task<string> transcription = _manager.TranscribeExistingRecordingAsync(
			service, session, string.Empty, CancellationToken.None);

		// Wait until the transcription is inside the service call, i.e. holding the lock.
		Assert.True(await serviceEntered.WaitAsync(TimeSpan.FromSeconds(10)));

		await Assert.ThrowsAsync<RecorderBusyException>(() =>
			_manager.StartRecordingAsync(0, lockTimeout: TimeSpan.FromMilliseconds(100)));

		releaseService.Release();
		Assert.Equal("done", await transcription);
	}

	[Fact]
	public async Task StartRecording_BusyTimeout_LeavesNoOrphanSessionFile()
	{
		string sessionPath = CreateSessionFile();
		var session = _manager.GetSessions().Single();

		using var serviceEntered = new SemaphoreSlim(0);
		using var releaseService = new SemaphoreSlim(0);
		var service = new BlockingSpeechToTextService(serviceEntered, releaseService);

		Task<string> transcription = _manager.TranscribeExistingRecordingAsync(
			service, session, string.Empty, CancellationToken.None);
		Assert.True(await serviceEntered.WaitAsync(TimeSpan.FromSeconds(10)));

		await Assert.ThrowsAsync<RecorderBusyException>(() =>
			_manager.StartRecordingAsync(0, lockTimeout: TimeSpan.FromMilliseconds(100)));

		releaseService.Release();
		await transcription;

		// Only the pre-existing session file may remain — the failed start must
		// not have created a new empty one.
		var remaining = _manager.GetSessions();
		Assert.Single(remaining);
		Assert.Equal(sessionPath, remaining[0].FilePath);
	}

	private sealed class BlockingSpeechToTextService : ISpeechToTextService
	{
		private readonly SemaphoreSlim _entered;
		private readonly SemaphoreSlim _release;

		public BlockingSpeechToTextService(SemaphoreSlim entered, SemaphoreSlim release)
		{
			_entered = entered;
			_release = release;
		}

		public string ServiceName { get; init; } = "Blocking";

		public async Task<string> ConvertAudioToText(string speechToTextPrompt, string audioffilePath, CancellationToken overallCancellationToken, int? timeoutSeconds = null)
		{
			_entered.Release();
			await _release.WaitAsync(overallCancellationToken);
			return "done";
		}
	}
}
