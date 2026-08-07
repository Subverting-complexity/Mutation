using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// The split-and-upload flow: what gets sent, in what order, what comes back, and what
/// is left on disk afterwards. The chunk writer is faked, so none of this needs a codec.
/// </summary>
public class ChunkedTranscriberTests : IDisposable
{
	private const long Limit = 1000;

	private readonly string _workingDirectory = Directory.CreateTempSubdirectory("chunked-transcriber-tests").FullName;

	public void Dispose()
	{
		try { Directory.Delete(_workingDirectory, recursive: true); } catch { }
	}

	private string WriteAudioFile(string name, int bytes)
	{
		string path = Path.Combine(_workingDirectory, name);
		File.WriteAllBytes(path, new byte[bytes]);
		return path;
	}

	private static Task<string> Transcribe(
		ChunkedTranscriber transcriber,
		ISpeechToTextService service,
		string audioPath,
		string workingRoot,
		CancellationToken token = default) =>
		transcriber.TranscribeAsync(service, "prompt", audioPath, Array.Empty<SilenceRemovalPoint>(), Limit, workingRoot, timeoutSeconds: 30, token);

	[Fact]
	public async Task FileInsideTheLimit_IsSentWhole_WithNoChunkingAtAll()
	{
		string audio = WriteAudioFile("small.ogg", (int)Limit);
		var writer = new FakeChunkWriter();
		var service = new RecordingService("all of it");

		string text = await Transcribe(new ChunkedTranscriber(writer), service, audio, _workingDirectory);

		Assert.Equal("all of it", text);
		Assert.Equal(new[] { audio }, service.Uploaded);
		Assert.False(writer.WasCalled);
	}

	[Fact]
	public async Task LimitOfZero_NeverSplits_HoweverBigTheFileIs()
	{
		string audio = WriteAudioFile("huge.ogg", 8000);
		var writer = new FakeChunkWriter();
		var service = new RecordingService("all of it");

		string text = await new ChunkedTranscriber(writer).TranscribeAsync(
			service, "prompt", audio, null, maxUploadBytes: 0, _workingDirectory, timeoutSeconds: 30, CancellationToken.None);

		Assert.Equal("all of it", text);
		Assert.Equal(new[] { audio }, service.Uploaded);
		Assert.False(writer.WasCalled);
	}

	[Fact]
	public async Task OversizedFile_IsUploadedChunkByChunk_AndTheTranscriptsJoinInOrder()
	{
		string audio = WriteAudioFile("big.ogg", 3000);
		var writer = new FakeChunkWriter(chunkSizes: new[] { 900, 900, 400 });
		var service = new RecordingService("first", "second", "third");

		string text = await Transcribe(new ChunkedTranscriber(writer), service, audio, _workingDirectory);

		Assert.Equal("first second third", text);
		Assert.Equal(3, service.Uploaded.Count);
		Assert.Equal(writer.WrittenPaths, service.Uploaded);
	}

	[Fact]
	public async Task BlankTranscripts_DoNotLeaveGapsInTheJoinedText()
	{
		string audio = WriteAudioFile("big.ogg", 3000);
		var writer = new FakeChunkWriter(chunkSizes: new[] { 900, 900, 400 });
		var service = new RecordingService("first", "   ", " third ");

		string text = await Transcribe(new ChunkedTranscriber(writer), service, audio, _workingDirectory);

		Assert.Equal("first third", text);
	}

	[Fact]
	public async Task TheChunkFolderIsGoneWhenTheUploadSucceeds()
	{
		string audio = WriteAudioFile("big.ogg", 3000);
		var writer = new FakeChunkWriter(chunkSizes: new[] { 900, 900 });

		await Transcribe(new ChunkedTranscriber(writer), new RecordingService("a", "b"), audio, _workingDirectory);

		Assert.False(Directory.Exists(writer.OutputDirectory!), "The chunk folder should not outlive the upload.");
	}

	[Fact]
	public async Task TheChunkFolderIsGoneWhenAnUploadFails()
	{
		string audio = WriteAudioFile("big.ogg", 3000);
		var writer = new FakeChunkWriter(chunkSizes: new[] { 900, 900 });
		var service = new RecordingService("a") { ThrowOnCall = 2 };

		await Assert.ThrowsAsync<HttpRequestFailure>(
			() => Transcribe(new ChunkedTranscriber(writer), service, audio, _workingDirectory));

		Assert.False(Directory.Exists(writer.OutputDirectory!), "A failed upload must not leave chunks behind.");
	}

	[Fact]
	public async Task CancellingPartWayThrough_StopsUploadingAndCleansUp()
	{
		string audio = WriteAudioFile("big.ogg", 3000);
		var writer = new FakeChunkWriter(chunkSizes: new[] { 900, 900, 900 });
		using var cts = new CancellationTokenSource();
		var service = new RecordingService("a", "b", "c") { CancelAfterCall = 1, Canceller = cts };

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => Transcribe(new ChunkedTranscriber(writer), service, audio, _workingDirectory, cts.Token));

		Assert.Single(service.Uploaded);
		Assert.False(Directory.Exists(writer.OutputDirectory!));
	}

	[Fact]
	public async Task AChunkOverTheLimit_IsRefusedBeforeAnythingIsUploaded()
	{
		string audio = WriteAudioFile("big.ogg", 3000);
		var writer = new FakeChunkWriter(chunkSizes: new[] { 900, (int)Limit + 1 });
		var service = new RecordingService("a", "b");

		var error = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Transcribe(new ChunkedTranscriber(writer), service, audio, _workingDirectory));

		Assert.Contains("upload limit", error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Empty(service.Uploaded);
	}

	[Fact]
	public async Task AnOversizedFileThatYieldsNoChunks_SaysSoRatherThanReturningNothing()
	{
		string audio = WriteAudioFile("big.ogg", 3000);
		var writer = new FakeChunkWriter(chunkSizes: Array.Empty<int>());
		var service = new RecordingService();

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => Transcribe(new ChunkedTranscriber(writer), service, audio, _workingDirectory));

		Assert.Empty(service.Uploaded);
	}

	[Fact]
	public async Task TheRemovalPointsForTheFileAreHandedToTheWriter()
	{
		string audio = WriteAudioFile("big.ogg", 3000);
		var writer = new FakeChunkWriter(chunkSizes: new[] { 900 });
		var points = new[] { new SilenceRemovalPoint(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2)) };

		await new ChunkedTranscriber(writer).TranscribeAsync(
			new RecordingService("a"), "prompt", audio, points, Limit, _workingDirectory, 30, CancellationToken.None);

		Assert.Equal(points, writer.ReceivedRemovalPoints);
		Assert.Equal(Limit, writer.ReceivedMaxChunkBytes);
	}

	private sealed class FakeChunkWriter : IAudioChunkWriter
	{
		private readonly int[] _chunkSizes;

		public FakeChunkWriter(int[]? chunkSizes = null) => _chunkSizes = chunkSizes ?? Array.Empty<int>();

		public bool WasCalled { get; private set; }
		public string? OutputDirectory { get; private set; }
		public long ReceivedMaxChunkBytes { get; private set; }
		public IReadOnlyList<SilenceRemovalPoint>? ReceivedRemovalPoints { get; private set; }
		public List<string> WrittenPaths { get; } = new();

		public IReadOnlyList<string> WriteChunks(
			string audioFilePath,
			IReadOnlyList<SilenceRemovalPoint>? removalPoints,
			long maxChunkBytes,
			string outputDirectory,
			CancellationToken cancellationToken)
		{
			WasCalled = true;
			OutputDirectory = outputDirectory;
			ReceivedMaxChunkBytes = maxChunkBytes;
			ReceivedRemovalPoints = removalPoints;

			Directory.CreateDirectory(outputDirectory);
			for (int i = 0; i < _chunkSizes.Length; i++)
			{
				string path = Path.Combine(outputDirectory, $"chunk-{i + 1:D3}.ogg");
				File.WriteAllBytes(path, new byte[_chunkSizes[i]]);
				WrittenPaths.Add(path);
			}

			return WrittenPaths.ToArray();
		}
	}

	private sealed class RecordingService : ISpeechToTextService
	{
		private readonly string[] _responses;
		private int _calls;

		public RecordingService(params string[] responses) => _responses = responses;

		public string ServiceName { get; init; } = "fake";
		public List<string> Uploaded { get; } = new();

		/// <summary>1-based call number that should throw instead of answering.</summary>
		public int ThrowOnCall { get; init; }

		/// <summary>1-based call number after which <see cref="Canceller"/> is tripped.</summary>
		public int CancelAfterCall { get; init; }
		public CancellationTokenSource? Canceller { get; init; }

		public Task<string> ConvertAudioToText(string speechToTextPrompt, string audioffilePath, CancellationToken overallCancellationToken, int? timeoutSeconds = null)
		{
			overallCancellationToken.ThrowIfCancellationRequested();

			_calls++;
			if (ThrowOnCall == _calls)
				throw new HttpRequestFailure();

			Uploaded.Add(audioffilePath);

			if (CancelAfterCall == _calls)
				Canceller?.Cancel();

			return Task.FromResult(_calls <= _responses.Length ? _responses[_calls - 1] : string.Empty);
		}
	}

	private sealed class HttpRequestFailure : Exception
	{
		public HttpRequestFailure() : base("the service said no") { }
	}
}
