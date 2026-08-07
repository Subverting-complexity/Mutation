using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CognitiveSupport;

/// <summary>
/// Sends an audio file for transcription, splitting it first when it is bigger than the
/// service will accept in one request.
///
/// A file inside the limit is sent exactly as before — one request, no decoding, no temp
/// files. Only an oversized file takes the long road: measure it, plan the cuts with
/// <see cref="AudioChunkPlanner"/>, write the chunks, check each one really is inside the
/// limit, then upload them one at a time in order and join what comes back. Sequential
/// rather than parallel on purpose: transcription endpoints rate-limit hard, and the
/// order of the transcript is the whole point.
/// </summary>
public sealed class ChunkedTranscriber
{
	private const string ChunkDirectoryPrefix = "chunks-";

	private readonly IAudioChunkWriter _chunkWriter;

	public ChunkedTranscriber()
		: this(new OggAudioChunkWriter())
	{
	}

	public ChunkedTranscriber(IAudioChunkWriter chunkWriter)
	{
		_chunkWriter = chunkWriter ?? throw new ArgumentNullException(nameof(chunkWriter));
	}

	/// <param name="removalPoints">
	/// Where silence was removed from this file, on its own (processed) timeline. Empty is
	/// fine — cuts then fall at the furthest safe position instead of at a pause.
	/// </param>
	/// <param name="maxUploadBytes">Per-request size limit. Zero or less disables splitting.</param>
	/// <param name="workingRootDirectory">Folder under which the temporary chunk folder is created.</param>
	public async Task<string> TranscribeAsync(
		ISpeechToTextService service,
		string prompt,
		string audioFilePath,
		IReadOnlyList<SilenceRemovalPoint>? removalPoints,
		long maxUploadBytes,
		string workingRootDirectory,
		int timeoutSeconds,
		CancellationToken cancellationToken)
	{
		if (service is null) throw new ArgumentNullException(nameof(service));
		if (string.IsNullOrWhiteSpace(audioFilePath))
			throw new ArgumentException("Audio file path cannot be empty.", nameof(audioFilePath));

		if (maxUploadBytes <= 0 || new FileInfo(audioFilePath).Length <= maxUploadBytes)
			return await service.ConvertAudioToText(prompt, audioFilePath, cancellationToken, timeoutSeconds).ConfigureAwait(false);

		string workingDirectory = Path.Combine(
			string.IsNullOrWhiteSpace(workingRootDirectory) ? Path.GetTempPath() : workingRootDirectory,
			ChunkDirectoryPrefix + Guid.NewGuid().ToString("N"));

		try
		{
			IReadOnlyList<string> chunks = await Task.Run(
				() => _chunkWriter.WriteChunks(audioFilePath, removalPoints, maxUploadBytes, workingDirectory, cancellationToken),
				cancellationToken).ConfigureAwait(false);

			if (chunks.Count == 0)
				throw new InvalidOperationException(
					$"'{Path.GetFileName(audioFilePath)}' is too large to send in one request and contains no audio to split.");

			VerifyWithinLimit(chunks, maxUploadBytes);

			var transcripts = new List<string>(chunks.Count);
			foreach (string chunk in chunks)
			{
				cancellationToken.ThrowIfCancellationRequested();

				string text = await service.ConvertAudioToText(prompt, chunk, cancellationToken, timeoutSeconds).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(text))
					transcripts.Add(text.Trim());
			}

			return string.Join(" ", transcripts);
		}
		finally
		{
			// Whether this succeeded, failed or was cancelled, the chunks were never
			// anything but scratch space for one upload.
			TryDeleteDirectory(workingDirectory);
		}
	}

	/// <summary>
	/// Last line of defence before anything is uploaded. The writer already caps each
	/// chunk as it encodes, so a failure here means that cap did not hold — better to say
	/// so than to let the service reject the request with something less specific.
	/// </summary>
	private static void VerifyWithinLimit(IReadOnlyList<string> chunks, long maxUploadBytes)
	{
		foreach (string chunk in chunks)
		{
			long length = new FileInfo(chunk).Length;
			if (length > maxUploadBytes)
				throw new InvalidOperationException(
					$"Audio chunk '{Path.GetFileName(chunk)}' is {length} bytes, over the {maxUploadBytes} byte upload limit.");
		}
	}

	private static void TryDeleteDirectory(string directory)
	{
		try
		{
			if (Directory.Exists(directory))
				Directory.Delete(directory, recursive: true);
		}
		catch (DirectoryNotFoundException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
		catch (PathTooLongException)
		{
		}
		catch (IOException)
		{
			// Scratch space in the app's temp folder; a file still locked by a failed
			// upload is not worth failing the transcription the user is waiting on.
		}
	}
}
