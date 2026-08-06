using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CognitiveSupport;

public static class AudioFileConverter
{
	private const int OutputSampleRate = 48000;

	// Opus requires 2.5, 5, 10, 20, 40, or 60ms frames. We use 20ms (960 samples at 48kHz).
	private const int SamplesPerFrame = 960;

	/// <summary>
	/// Converts a video/audio file to OGG Opus format and saves to a temp file.
	/// </summary>
	/// <param name="inputPath">Path to the input file (MP4, MP3, OGG/Opus, etc.)</param>
	/// <returns>Path to the temporary OGG file. Caller is responsible for cleanup.</returns>
	public static string ConvertToOgg(string inputPath, SilenceTrimmerOptions? silenceOptions = null)
	{
		// Not Path.GetTempFileName(): that *creates* a zero-byte .tmp on disk, and
		// ChangeExtension then returns a different path string and walks away from it,
		// leaking one stray file per call.
		string tempOggPath = Path.Combine(Path.GetTempPath(), Path.ChangeExtension(Path.GetRandomFileName(), ".ogg"));
		try
		{
			ConvertToOgg(inputPath, tempOggPath, silenceOptions);
			return tempOggPath;
		}
		catch
		{
			if (File.Exists(tempOggPath))
			{
				try { File.Delete(tempOggPath); } catch { }
			}
			throw;
		}
	}

	/// <summary>
	/// Converts a video/audio file to OGG Opus format and saves to the specified output path.
	/// </summary>
	/// <param name="inputPath">Path to the input file (MP4, MP3, OGG/Opus, etc.)</param>
	/// <param name="outputOggPath">Path where the OGG file will be written.</param>
	public static void ConvertToOgg(string inputPath, string outputOggPath, SilenceTrimmerOptions? silenceOptions = null)
	{
		if (string.IsNullOrWhiteSpace(inputPath))
			throw new ArgumentException("Input path cannot be empty", nameof(inputPath));

		if (string.IsNullOrWhiteSpace(outputOggPath))
			throw new ArgumentException("Output path cannot be empty", nameof(outputOggPath));

		if (!File.Exists(inputPath))
			throw new FileNotFoundException("Input file not found", inputPath);

		using var outStream = new FileStream(outputOggPath, FileMode.Create, FileAccess.Write, FileShare.None);
#pragma warning disable CS0618 // Concentus.OggFile 1.0.6 still requires the concrete OpusEncoder type, not IOpusEncoder from OpusCodecFactory.
		var encoder = new OpusEncoder(OutputSampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP)
		{
			Bitrate = 24000,
			UseVBR = true,
			UseDTX = false // DTX not supported in Ogg streams by Concentus.OggFile
		};
#pragma warning restore CS0618
		var tags = new OpusTags();
		var oggStream = new OpusOggWriteStream(encoder, outStream, tags);

		var splitter = new PcmFrameSplitter(SamplesPerFrame);

		var trimmer = silenceOptions is null
			? null
			: new SilenceTrimmer(OutputSampleRate, SamplesPerFrame, silenceOptions);

		void WriteFrame(short[] pcmSamples)
		{
			if (trimmer is null)
				oggStream.WriteSamples(pcmSamples, 0, pcmSamples.Length);
			else
				trimmer.ProcessFrame(pcmSamples, f => oggStream.WriteSamples(f, 0, f.Length));
		}

		// Media Foundation cannot demux Ogg/Opus, so those files are decoded in managed code
		// instead. Everything else goes through Media Foundation as before.
		if (OggOpusPcmDecoder.IsOggOpus(inputPath))
			DecodeOggOpus(inputPath, splitter, WriteFrame);
		else
			DecodeWithMediaFoundation(inputPath, splitter, WriteFrame);

		// Pad the final partial frame with silence and emit it, matching the previous
		// behavior of never dropping trailing audio on file conversion.
		splitter.Flush(WriteFrame, padWithSilence: true);

		trimmer?.Flush(f => oggStream.WriteSamples(f, 0, f.Length));

		oggStream.Finish();
	}

	private static void DecodeOggOpus(string inputPath, PcmFrameSplitter splitter, Action<short[]> writeFrame)
	{
		OggOpusPcmDecoder.DecodeToMonoPcm(inputPath, packet => splitter.AppendSamples(packet, packet.Length, writeFrame));
	}

	private static void DecodeWithMediaFoundation(string inputPath, PcmFrameSplitter splitter, Action<short[]> writeFrame)
	{
		using var reader = OpenMediaFoundationReader(inputPath);

		// Target format: 48kHz, Mono, 16-bit
		var outFormat = new WaveFormat(OutputSampleRate, 16, 1);

		using var resampler = new MediaFoundationResampler(reader, outFormat);
		resampler.ResamplerQuality = 60; // Reasonable quality

		// Buffer for reading from resampler (1 second worth of audio)
		byte[] buffer = new byte[outFormat.AverageBytesPerSecond];
		int bytesRead;

		while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
		{
			splitter.Append(buffer, bytesRead, writeFrame);
		}
	}

	/// <summary>
	/// Opens the file with Media Foundation, translating its bare HRESULT failures (typically
	/// MF_E_UNSUPPORTED_BYTESTREAM_TYPE, 0xC00D36C4, for formats Windows has no demuxer for
	/// such as Ogg/Vorbis or some WebM) into a message that tells the user what to do next.
	/// </summary>
	private static MediaFoundationReader OpenMediaFoundationReader(string inputPath)
	{
		try
		{
			return new MediaFoundationReader(inputPath);
		}
		catch (COMException ex)
		{
			string extension = Path.GetExtension(inputPath);
			string formatLabel = string.IsNullOrWhiteSpace(extension) ? "this format" : extension.ToLowerInvariant();
			throw new InvalidOperationException(
				$"Windows cannot decode '{Path.GetFileName(inputPath)}' ({formatLabel}). " +
				"Convert it to WAV, MP3, MP4, or Ogg/Opus first.", ex);
		}
	}

	public static bool IsVideoFile(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath)) return false;
		string ext = Path.GetExtension(filePath);
		return string.Equals(ext, ".mp4", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(ext, ".avi", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(ext, ".mkv", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(ext, ".mov", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(ext, ".wmv", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(ext, ".m4v", StringComparison.OrdinalIgnoreCase);
	}
}
