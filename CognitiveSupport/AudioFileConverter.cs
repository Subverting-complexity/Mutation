using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;
using System;
using System.IO;

namespace CognitiveSupport;

public static class AudioFileConverter
{
	/// <summary>
	/// Converts a video/audio file to OGG Opus format and saves to a temp file.
	/// </summary>
	/// <param name="inputPath">Path to the input file (MP4, AVI, etc.)</param>
	/// <returns>Path to the temporary OGG file. Caller is responsible for cleanup.</returns>
	public static string ConvertMp4ToOgg(string inputPath, SilenceTrimmerOptions? silenceOptions = null)
	{
		string tempOggPath = Path.ChangeExtension(Path.GetTempFileName(), ".ogg");
		try
		{
			ConvertMp4ToOgg(inputPath, tempOggPath, silenceOptions);
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
	/// <param name="inputPath">Path to the input file (MP4, AVI, etc.)</param>
	/// <param name="outputOggPath">Path where the OGG file will be written.</param>
	public static void ConvertMp4ToOgg(string inputPath, string outputOggPath, SilenceTrimmerOptions? silenceOptions = null)
	{
		if (string.IsNullOrWhiteSpace(inputPath))
			throw new ArgumentException("Input path cannot be empty", nameof(inputPath));

		if (string.IsNullOrWhiteSpace(outputOggPath))
			throw new ArgumentException("Output path cannot be empty", nameof(outputOggPath));

		if (!File.Exists(inputPath))
			throw new FileNotFoundException("Input file not found", inputPath);

		using var reader = new MediaFoundationReader(inputPath);
		
		// Target format: 48kHz, Mono, 16-bit
		var outFormat = new WaveFormat(48000, 16, 1);
		
		using var resampler = new MediaFoundationResampler(reader, outFormat);
		resampler.ResamplerQuality = 60; // Reasonable quality

		using var outStream = new FileStream(outputOggPath, FileMode.Create, FileAccess.Write, FileShare.None);
#pragma warning disable CS0618 // Concentus.OggFile 1.0.6 still requires the concrete OpusEncoder type, not IOpusEncoder from OpusCodecFactory.
		var encoder = new OpusEncoder(48000, 1, OpusApplication.OPUS_APPLICATION_VOIP)
		{
			Bitrate = 24000,
			UseVBR = true,
			UseDTX = false // DTX not supported in Ogg streams by Concentus.OggFile
		};
#pragma warning restore CS0618
		var tags = new OpusTags();
		var oggStream = new OpusOggWriteStream(encoder, outStream, tags);

		// Buffer for reading from resampler (1 second worth of audio)
		byte[] buffer = new byte[outFormat.AverageBytesPerSecond];
		int bytesRead;

		// Split the decoded byte stream into fixed frame sizes for Opus (960 samples = 1920
		// bytes). Opus requires 2.5, 5, 10, 20, 40, or 60ms frames. We use 20ms (960 samples).
		int samplesPerFrame = 960;
		var splitter = new PcmFrameSplitter(samplesPerFrame);

		var trimmer = silenceOptions is null
			? null
			: new SilenceTrimmer(48000, samplesPerFrame, silenceOptions);

		void WriteFrame(short[] pcmSamples)
		{
			if (trimmer is null)
				oggStream.WriteSamples(pcmSamples, 0, pcmSamples.Length);
			else
				trimmer.ProcessFrame(pcmSamples, f => oggStream.WriteSamples(f, 0, f.Length));
		}

		while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
		{
			splitter.Append(buffer, bytesRead, WriteFrame);
		}

		// Pad the final partial frame with silence and emit it, matching the previous
		// behavior of never dropping trailing audio on file conversion.
		splitter.Flush(WriteFrame, padWithSilence: true);

		trimmer?.Flush(f => oggStream.WriteSamples(f, 0, f.Length));

		oggStream.Finish();
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
