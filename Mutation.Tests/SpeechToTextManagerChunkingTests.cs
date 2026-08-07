using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;
using Mutation.Ui;

namespace Mutation.Tests;

/// <summary>
/// The wiring between the manager and the chunker: that the configured upload limit is
/// the one actually applied, and that the pauses stripped out of a recording reach the
/// code deciding where to cut it. The chunk writer is faked, so no codec is involved.
/// </summary>
public class SpeechToTextManagerChunkingTests : IDisposable
{
	// The domain clamps anything under a megabyte up to it, so that is the smallest limit
	// a test can actually exercise.
	private const long Limit = SpeechToTextSettings.MinTranscriptionUploadBytes;

	private readonly string _tempDirectory;
	private readonly Settings _settings;

	public SpeechToTextManagerChunkingTests()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), "MutationTests_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDirectory);
		_settings = new Settings
		{
			SpeechToTextSettings = new SpeechToTextSettings
			{
				TempDirectory = _tempDirectory,
				MaxTranscriptionUploadBytes = Limit,
			}
		};
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDirectory, recursive: true); } catch { }
	}

	private SpeechSession WriteSession(string fileName, int bytes)
	{
		string directory = Path.Combine(_tempDirectory, Constants.SessionsDirectoryName);
		Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, fileName);
		File.WriteAllBytes(path, new byte[bytes]);
		return new SpeechSession(path, new DateTime(2024, 1, 1));
	}

	private static SpeechToTextManager NewManager(Settings settings, IAudioChunkWriter writer, Func<IAudioRecorder>? recorderFactory = null) =>
		new(settings, recorderFactory ?? (() => new SilentRecorder()), new ChunkedTranscriber(writer));

	[Fact]
	public async Task ASessionOverTheConfiguredLimit_IsSplitBeforeItIsSent()
	{
		var session = WriteSession("session_2024-01-01_00-00-00.ogg", (int)Limit + 1);
		var writer = new FakeChunkWriter(chunkCount: 2);
		var service = new StubService("one", "two");

		using var manager = NewManager(_settings, writer);
		string text = await manager.TranscribeExistingRecordingAsync(service, session, "prompt", CancellationToken.None);

		Assert.Equal("one two", text);
		Assert.Equal(Limit, writer.ReceivedMaxChunkBytes);
		Assert.Equal(2, service.Uploaded.Count);
	}

	[Fact]
	public async Task ASessionInsideTheConfiguredLimit_IsSentWhole()
	{
		var session = WriteSession("session_2024-01-01_00-00-00.ogg", (int)Limit);
		var writer = new FakeChunkWriter(chunkCount: 2);
		var service = new StubService("whole thing");

		using var manager = NewManager(_settings, writer);
		string text = await manager.TranscribeExistingRecordingAsync(service, session, "prompt", CancellationToken.None);

		Assert.Equal("whole thing", text);
		Assert.False(writer.WasCalled);
		Assert.Equal(new[] { session.FilePath }, service.Uploaded);
	}

	[Fact]
	public async Task AnOutOfRangeLimitInTheSettingsFile_IsClampedRatherThanObeyed()
	{
		// 12 bytes would make every chunk unsendable; the domain clamp raises it to 1 MB,
		// which this session is comfortably inside, so it goes as one request.
		_settings.SpeechToTextSettings!.MaxTranscriptionUploadBytes = 12;
		var session = WriteSession("session_2024-01-01_00-00-00.ogg", 4096);
		var writer = new FakeChunkWriter(chunkCount: 2);
		var service = new StubService("whole thing");

		using var manager = NewManager(_settings, writer);
		await manager.TranscribeExistingRecordingAsync(service, session, "prompt", CancellationToken.None);

		Assert.False(writer.WasCalled);
	}

	[Fact]
	public async Task ThePausesStrippedOutOfARecording_ReachTheCodeThatChoosesTheCuts()
	{
		var removals = new[]
		{
			new SilenceRemovalPoint(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(4)),
			new SilenceRemovalPoint(TimeSpan.FromSeconds(31), TimeSpan.FromSeconds(9)),
		};
		var writer = new FakeChunkWriter(chunkCount: 2);

		using var manager = NewManager(_settings, writer, () => new SilentRecorder(removals, oversizedBytes: (int)Limit + 1));
		await manager.StartRecordingAsync(0);

		await manager.StopRecordingAndTranscribeAsync(
			new StubService("one", "two"), "prompt", CancellationToken.None, onRecordingStopped: () => { });

		Assert.Equal(removals, writer.ReceivedRemovalPoints);
	}

	private sealed class FakeChunkWriter : IAudioChunkWriter
	{
		private readonly int _chunkCount;

		public FakeChunkWriter(int chunkCount) => _chunkCount = chunkCount;

		public bool WasCalled { get; private set; }
		public long ReceivedMaxChunkBytes { get; private set; }
		public IReadOnlyList<SilenceRemovalPoint>? ReceivedRemovalPoints { get; private set; }

		public IReadOnlyList<string> WriteChunks(
			string audioFilePath,
			IReadOnlyList<SilenceRemovalPoint>? removalPoints,
			long maxChunkBytes,
			string outputDirectory,
			CancellationToken cancellationToken)
		{
			WasCalled = true;
			ReceivedMaxChunkBytes = maxChunkBytes;
			ReceivedRemovalPoints = removalPoints;

			Directory.CreateDirectory(outputDirectory);
			var paths = new List<string>();
			for (int i = 0; i < _chunkCount; i++)
			{
				string path = Path.Combine(outputDirectory, $"chunk-{i + 1:D3}.ogg");
				File.WriteAllBytes(path, new byte[8]);
				paths.Add(path);
			}

			return paths;
		}
	}

	// Writes nothing while "recording"; on stop it fills the session file so the manager
	// sees a file of the size the test wants.
	private sealed class SilentRecorder : IAudioRecorder
	{
		private readonly int _oversizedBytes;
		private string? _outputFile;

		public SilentRecorder() : this(Array.Empty<SilenceRemovalPoint>(), oversizedBytes: 0) { }

		public SilentRecorder(IReadOnlyList<SilenceRemovalPoint> removedSilences, int oversizedBytes)
		{
			RemovedSilences = removedSilences;
			_oversizedBytes = oversizedBytes;
		}

		public double? TrimmedSpeechSeconds => null;
		public Exception? CaptureException => null;
		public IReadOnlyList<SilenceRemovalPoint> RemovedSilences { get; }

		public void StartRecording(int captureDeviceIndex, string outputFile, SilenceTrimmerOptions? silenceOptions = null) =>
			_outputFile = outputFile;

		public void StopRecording()
		{
			if (_oversizedBytes > 0 && _outputFile is not null)
				File.WriteAllBytes(_outputFile, new byte[_oversizedBytes]);
		}

		public void Dispose() { }
	}

	private sealed class StubService : ISpeechToTextService
	{
		private readonly string[] _responses;
		private int _calls;

		public StubService(params string[] responses) => _responses = responses;

		public string ServiceName { get; init; } = "fake";
		public List<string> Uploaded { get; } = new();

		public Task<string> ConvertAudioToText(string speechToTextPrompt, string audioffilePath, CancellationToken overallCancellationToken, int? timeoutSeconds = null)
		{
			Uploaded.Add(audioffilePath);
			_calls++;
			return Task.FromResult(_calls <= _responses.Length ? _responses[_calls - 1] : string.Empty);
		}
	}
}
