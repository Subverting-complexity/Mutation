using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;
using System.IO;

namespace CognitiveSupport;

public class AudioRecorder : IAudioRecorder
{
	private WaveInEvent? _waveIn;
	private OpusEncoder? _encoder;
	private OpusOggWriteStream? _oggStream;
	private Stream? _fileStream;
	private SilenceTrimmer? _silenceTrimmer;
	private readonly object _writeLock = new();
	private Exception? _captureException;

	// After StopRecording, the trimmed speech duration in seconds when silence stripping was
	// active; null when stripping was disabled (so callers leave the recording untouched).
	public double? TrimmedSpeechSeconds { get; private set; }

	// First exception (if any) that occurred while encoding audio on NAudio's capture
	// thread. Captured rather than thrown there, where it would escape as an unhandled
	// exception and terminate the process. Callers surface it after StopRecording.
	public Exception? CaptureException
	{
		get { lock (_writeLock) return _captureException; }
	}

	// Opus requires specific frame sizes. 20ms at 48kHz = 960 samples.
	private const int SampleRate = 48000;
	private const int FrameSizeMs = 20;
	private const int SamplesPerFrame = SampleRate * FrameSizeMs / 1000; // 960 samples
	private const int Channels = 1;

	// Splits incoming PCM bytes into fixed 960-sample frames. Created per recording in
	// StartRecording so no partial-frame remainder bleeds from one recording into the next.
	private PcmFrameSplitter? _pcmFrameSplitter;

	public void StartRecording(int captureDeviceIndex, string outputFile, SilenceTrimmerOptions? silenceOptions = null)
	{
		lock (_writeLock)
		{
			_silenceTrimmer = silenceOptions is null
				? null
				: new SilenceTrimmer(SampleRate, SamplesPerFrame, silenceOptions);
			_pcmFrameSplitter = new PcmFrameSplitter(SamplesPerFrame);
			TrimmedSpeechSeconds = null;
			_captureException = null;

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

#pragma warning disable CS0618 // Concentus.OggFile 1.0.6 still requires the concrete OpusEncoder type, not IOpusEncoder from OpusCodecFactory.
				encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP)
				{
					Bitrate = 24000,
					UseVBR = true,
					UseDTX = false // DTX not supported in Ogg streams by Concentus.OggFile
				};
#pragma warning restore CS0618

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
			if (_oggStream == null || _pcmFrameSplitter is null) return;

			try
			{
				// Split incoming 16-bit PCM bytes into whole 960-sample frames and write each.
				_pcmFrameSplitter.Append(e.Buffer, e.BytesRecorded, EmitFrame);
			}
			catch (Exception ex)
			{
				// This runs on NAudio's capture thread; letting it propagate would be an
				// unhandled exception that terminates the process. Capture the first
				// failure and stop writing so StopRecording can surface it to the caller.
				_captureException ??= ex;
				_pcmFrameSplitter = null;
				_oggStream = null;
			}
		}
	}

	private void EmitFrame(short[] frame)
	{
		if (_silenceTrimmer is null)
			_oggStream?.WriteSamples(frame, 0, frame.Length);
		else
			_silenceTrimmer.ProcessFrame(frame, WriteFrame);
	}

	private void WriteFrame(short[] frame) => _oggStream?.WriteSamples(frame, 0, frame.Length);

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

					if (_silenceTrimmer is not null)
					{
						_silenceTrimmer.Flush(WriteFrame);
						TrimmedSpeechSeconds = _silenceTrimmer.SpeechFrameCount * SamplesPerFrame / (double)SampleRate;
					}

					_oggStream.Finish();
				}
				finally
				{
					_oggStream = null;
					_silenceTrimmer = null;
					// Drop any buffered partial frame (< 20ms tail), as before.
					_pcmFrameSplitter = null;
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
