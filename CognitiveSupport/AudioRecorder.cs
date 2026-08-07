using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;
using System.IO;
using System.Linq;

namespace CognitiveSupport;

public class AudioRecorder : IAudioRecorder
{
	private readonly Func<int, IAudioCaptureSource> _captureSourceFactory;
	private IAudioCaptureSource? _waveIn;
	private OpusEncoder? _encoder;
	private OpusOggWriteStream? _oggStream;
	private Stream? _fileStream;
	private SilenceTrimmer? _silenceTrimmer;
	private readonly object _writeLock = new();
	private Exception? _captureException;
	private bool _disposed;

	// Set when the capture source reports that it has finished delivering buffers. Starts
	// signalled so a StopRecording with nothing running never waits.
	private readonly ManualResetEventSlim _captureDrained = new(initialState: true);

	// How long StopRecording will wait for the capture thread to hand over its last buffers.
	// Bounded so a wedged audio driver costs a short pause rather than a hung app; the
	// real drain takes about one buffer's worth of time.
	private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

	// After StopRecording, the trimmed speech duration in seconds when silence stripping was
	// active; null when stripping was disabled (so callers leave the recording untouched).
	public double? TrimmedSpeechSeconds { get; private set; }

	// After StopRecording, the silences the trimmer stripped out, positioned on the
	// written file's timeline. Captured before the trimmer is dropped so the chunker can
	// still cut at a pause once the recording is closed.
	public IReadOnlyList<SilenceRemovalPoint> RemovedSilences { get; private set; } = Array.Empty<SilenceRemovalPoint>();

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

	// Larger buffers from NAudio, to reduce per-callback overhead.
	private const int BufferMilliseconds = 100;

	// Splits incoming PCM bytes into fixed 960-sample frames. Created per recording in
	// StartRecording so no partial-frame remainder bleeds from one recording into the next.
	private PcmFrameSplitter? _pcmFrameSplitter;

	public AudioRecorder()
		: this(deviceIndex => new WaveInCaptureSource(
			deviceIndex,
			new WaveFormat(SampleRate, 16, Channels),
			BufferMilliseconds))
	{
	}

	internal AudioRecorder(Func<int, IAudioCaptureSource> captureSourceFactory)
	{
		_captureSourceFactory = captureSourceFactory ?? throw new ArgumentNullException(nameof(captureSourceFactory));
	}

	public void StartRecording(int captureDeviceIndex, string outputFile, SilenceTrimmerOptions? silenceOptions = null)
	{
		lock (_writeLock)
		{
			_silenceTrimmer = silenceOptions is null
				? null
				: new SilenceTrimmer(SampleRate, SamplesPerFrame, silenceOptions);
			_pcmFrameSplitter = new PcmFrameSplitter(SamplesPerFrame);
			TrimmedSpeechSeconds = null;
			RemovedSilences = Array.Empty<SilenceRemovalPoint>();
			_captureException = null;

			// A source left over from a previous recording still holds the microphone open, and
			// its handlers still point here: its buffers would land in the new recording and
			// its RecordingStopped would open the new drain gate early.
			DetachCaptureSource();

			IAudioCaptureSource? waveIn = null;
			Stream? fileStream = null;
			OpusEncoder? encoder = null;
			OpusOggWriteStream? oggStream = null;
			try
			{
				waveIn = _captureSourceFactory(captureDeviceIndex);

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

				// Armed only once the source is about to run, so a start that throws leaves the
				// gate open rather than making the next StopRecording wait out the timeout.
				_captureDrained.Reset();

				try
				{
					_waveIn.StartRecording();
				}
				catch
				{
					// Roll back field commits so subsequent calls / Dispose see clean state.
					_captureDrained.Set();
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
		// Every buffer the capture thread had in hand has now been delivered, so StopRecording
		// can safely close the encoder. Cleanup itself happens in Dispose or explicit Stop.
		_captureDrained.Set();
	}

	public void StopRecording()
	{
		IAudioCaptureSource? capture;
		lock (_writeLock)
		{
			if (_disposed)
				return;

			capture = _waveIn;
		}

		if (capture is not null)
		{
			// StopRecording only *signals* the capture thread: buffers it has already filled
			// are delivered to OnDataAvailable afterwards, on that thread. Finishing the Ogg
			// stream before they arrive dropped them silently against the null guard in
			// OnDataAvailable, cutting the last fraction of a second — often the last word —
			// off every recording. Wait for the drain, outside _writeLock so those deliveries
			// can still take it.
			capture.StopRecording();
			try
			{
				_captureDrained.Wait(DrainTimeout);

				// Whether the source reported in or the wait timed out, this recording's
				// drain is over. Latching it stops the repeat StopRecording that Dispose
				// makes from sitting through the timeout a second time.
				_captureDrained.Set();
			}
			catch (ObjectDisposedException)
			{
				// Another thread disposed the recorder while we were waiting. There is
				// nothing left to drain into, so fall through and let the checks below
				// find the encoder already gone.
			}
		}

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

						// Copied out: the trimmer is dropped in the finally below, and the
						// list it hands back is the one it keeps appending to.
						RemovedSilences = _silenceTrimmer.RemovedSilences.ToArray();
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

	// Unhooks and releases the current capture source. Callers must hold _writeLock.
	private void DetachCaptureSource()
	{
		if (_waveIn is null)
			return;

		_waveIn.DataAvailable -= OnDataAvailable;
		_waveIn.RecordingStopped -= OnRecordingStopped;
		try { _waveIn.Dispose(); } catch { /* Releasing a dead device must not fail the caller. */ }
		_waveIn = null;
	}

	public void Dispose()
	{
		StopRecording();

		lock (_writeLock)
		{
			if (_disposed)
				return;

			DetachCaptureSource();

			// _oggStream does not implement IDisposable but it uses the stream.
			// The stream is disposed here.
			_fileStream?.Dispose();
			_fileStream = null;

			_encoder = null;
			_oggStream = null;

			_disposed = true;

			// Released the moment nothing can arm it again. A StopRecording that slipped past
			// the _disposed check and is waiting here catches the ObjectDisposedException.
			_captureDrained.Set();
			_captureDrained.Dispose();
		}
	}
}
