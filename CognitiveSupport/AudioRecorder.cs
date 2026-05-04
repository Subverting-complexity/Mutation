using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;
using System.IO;

namespace CognitiveSupport;

public class AudioRecorder : IDisposable
{
	private WaveInEvent? _waveIn;
	private OpusEncoder? _encoder;
	private OpusOggWriteStream? _oggStream;
	private Stream? _fileStream;
	private readonly object _writeLock = new();

	// Opus requires specific frame sizes. 20ms at 48kHz = 960 samples.
	private const int SampleRate = 48000;
	private const int FrameSizeMs = 20;
	private const int SamplesPerFrame = SampleRate * FrameSizeMs / 1000; // 960 samples
	private const int Channels = 1;

	// Buffer for incoming PCM data
	private readonly List<short> _pcmBuffer = new();

	public void StartRecording(int captureDeviceIndex, string outputFile)
	{
		lock (_writeLock)
		{
			WaveInEvent? waveIn = null;
			Stream? fileStream = null;
			OpusEncoder? encoder = null;
			OpusOggWriteStream? oggStream = null;
			try
			{
				waveIn = new WaveInEvent
				{
					DeviceNumber = captureDeviceIndex,
					WaveFormat = new WaveFormat(SampleRate, 16, Channels),
					BufferMilliseconds = 100 // Request larger buffers from NAudio to reduce overhead
				};

				fileStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.Read);

				encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP)
				{
					Bitrate = 24000,
					UseVBR = true,
					UseDTX = false // DTX not supported in Ogg streams by Concentus.OggFile
				};

				oggStream = new OpusOggWriteStream(encoder, fileStream, new OpusTags());

				waveIn.DataAvailable += OnDataAvailable;
				waveIn.RecordingStopped += OnRecordingStopped;

				// Commit to fields BEFORE StartRecording so OnDataAvailable can find _oggStream.
				_waveIn = waveIn;
				_fileStream = fileStream;
				_encoder = encoder;
				_oggStream = oggStream;

				try
				{
					_waveIn.StartRecording();
				}
				catch
				{
					// Roll back field commits so subsequent calls / Dispose see clean state.
					_waveIn = null;
					_fileStream = null;
					_encoder = null;
					_oggStream = null;
					throw;
				}
			}
			catch
			{
				// Clean up anything allocated but not committed (or rolled back above).
				try { if (waveIn is not null && _waveIn is null) waveIn.Dispose(); } catch { }
				try { if (fileStream is not null && _fileStream is null) fileStream.Dispose(); } catch { }
				// OpusEncoder / OpusOggWriteStream don't implement IDisposable in Concentus.
				throw;
			}
		}
	}

	private void OnDataAvailable(object? sender, WaveInEventArgs e)
	{
		lock (_writeLock)
		{
			if (_oggStream == null) return;

			// Convert bytes to shorts (16-bit PCM)
			// e.BytesRecorded is count of bytes. 2 bytes per sample.
			int incomingSamples = e.BytesRecorded / 2;
			for (int i = 0; i < incomingSamples; i++)
			{
				short sample = (short)((e.Buffer[i * 2 + 1] << 8) | e.Buffer[i * 2]);
				_pcmBuffer.Add(sample);
			}

			// Process complete frames
			while (_pcmBuffer.Count >= SamplesPerFrame)
			{
				var frame = _pcmBuffer.GetRange(0, SamplesPerFrame).ToArray();
				_pcmBuffer.RemoveRange(0, SamplesPerFrame);
				
				_oggStream.WriteSamples(frame, 0, SamplesPerFrame);
			}
		}
	}

	private void OnRecordingStopped(object? sender, StoppedEventArgs e)
	{
		// We handle cleanup/finishing in Dispose or explicit Stop
	}

	public void StopRecording()
	{
		_waveIn?.StopRecording();
		
		lock (_writeLock)
		{
			if (_oggStream != null)
			{
				try
				{
					// Flush remaining samples if needed? 
					// Opus generally works on whole frames. If we have leftover samples < 20ms,
					// we could pad with silence or just discard.
					// For voice, discarding < 20ms at the end is usually fine.
					
					_oggStream.Finish();
				}
				finally
				{
					_oggStream = null;
				}
			}
		}
	}

	public void Dispose()
	{
		StopRecording();

		lock (_writeLock)
		{
			_waveIn?.Dispose();
			_waveIn = null;

			// _oggStream does not implement IDisposable but it uses the stream.
			// The stream is disposed here.
			_fileStream?.Dispose();
			_fileStream = null;
			
			_encoder = null;
			_oggStream = null;
		}
	}
}
