using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CognitiveSupport;

/// <summary>
/// Audio player that supports OGG/Opus files using NAudio and Concentus.
/// This bypasses Windows Media Foundation which doesn't natively support OGG/Opus.
///
/// Every format flows through a single chain: the decoded/read audio becomes a
/// common sample source, passes through a pitch-preserving time-stretch stage
/// (<see cref="SoundTouchSampleProvider"/>), and is then sent to the output
/// device. That lets <see cref="Speed"/> change how fast playback runs — live,
/// without restarting — while the voice keeps its natural pitch.
/// </summary>
public class AudioPlayer : IDisposable
{
    private WaveOutEvent? _waveOut;
    private MemoryStream? _pcmStream;
    private WaveStream? _sourceStream;
    private SoundTouchSampleProvider? _speedProvider;
    private readonly object _playLock = new();
    private double _speed = PlaybackSpeedOptions.Default;
    private bool _disposed;

    // Opus parameters matching AudioRecorder
    private const int SampleRate = 48000;
    private const int Channels = 1;

    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

    /// <summary>
    /// Playback speed multiplier (1.0 = normal). Any value is snapped to the
    /// nearest supported speed. Setting it applies immediately to audio that is
    /// already playing — without stopping, restarting, or losing position — and
    /// is remembered for the next file played.
    /// </summary>
    public double Speed
    {
        get { lock (_playLock) { return _speed; } }
        set
        {
            lock (_playLock)
            {
                double normalized = PlaybackSpeedOptions.Normalize(value);
                _speed = normalized;
                if (_speedProvider != null)
                    _speedProvider.Tempo = normalized;
            }
        }
    }

    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackFailed;

    /// <summary>
    /// Plays an audio file. Supports OGG/Opus, WAV, and MP3 formats.
    /// </summary>
    public void Play(string filePath)
    {
        lock (_playLock)
        {
            Stop();

            try
            {
                string extension = Path.GetExtension(filePath).ToLowerInvariant();

                if (extension == ".ogg" || extension == ".opus")
                {
                    PlayOgg(filePath);
                }
                else
                {
                    // For other formats, use NAudio's built-in readers
                    PlayWithNAudio(filePath, extension);
                }
            }
            catch (Exception ex)
            {
                Stop();
                PlaybackFailed?.Invoke(this, $"Playback failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Plays an OGG/Opus file by decoding it with <see cref="OggOpusPcmDecoder"/> and playing
    /// via NAudio. The decoder is shared with the import path so files this app did not record
    /// itself — WhatsApp voice notes and the like — play back rather than hanging.
    /// </summary>
    private void PlayOgg(string filePath)
    {
        // Read and decode the entire OGG file to PCM
        var allSamples = new List<short>();
        OggOpusPcmDecoder.DecodeToMonoPcm(filePath, samples => allSamples.AddRange(samples));

        if (allSamples.Count == 0)
        {
            PlaybackFailed?.Invoke(this, "No audio data found in file.");
            return;
        }

        // Convert shorts to bytes (16-bit PCM)
        var pcmBytes = new byte[allSamples.Count * 2];
        for (int i = 0; i < allSamples.Count; i++)
        {
            var sample = allSamples[i];
            pcmBytes[i * 2] = (byte)(sample & 0xFF);
            pcmBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        _pcmStream = new MemoryStream(pcmBytes);
        var waveFormat = new WaveFormat(SampleRate, 16, Channels);
        StartPlayback(new RawSourceWaveStream(_pcmStream, waveFormat));
    }

    /// <summary>
    /// Plays other audio formats using NAudio's built-in readers.
    /// </summary>
    private void PlayWithNAudio(string filePath, string extension)
    {
        WaveStream reader = extension switch
        {
            ".wav" => new WaveFileReader(filePath),
            ".mp3" => new Mp3FileReader(filePath),
            _ => new AudioFileReader(filePath) // Generic reader for other formats
        };

        StartPlayback(reader);
    }

    /// <summary>
    /// Wraps a format-specific audio source in the pitch-preserving speed stage and
    /// starts playback. Shared by every playback path so the speed control applies
    /// uniformly to OGG/Opus, WAV, MP3, and any other supported format.
    /// </summary>
    private void StartPlayback(WaveStream source)
    {
        _sourceStream = source;

        // ToSampleProvider normalises every format to float samples; SoundTouch then
        // time-stretches them, and SampleToWaveProvider converts back for output.
        _speedProvider = new SoundTouchSampleProvider(source.ToSampleProvider()) { Tempo = _speed };
        IWaveProvider output = new SampleToWaveProvider(_speedProvider);

        _waveOut = new WaveOutEvent();
        _waveOut.PlaybackStopped += OnPlaybackStopped;
        _waveOut.Init(output);
        _waveOut.Play();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            PlaybackFailed?.Invoke(this, $"Playback error: {e.Exception.Message}");
        }
        else
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Stop()
    {
        lock (_playLock)
        {
            try
            {
                if (_waveOut != null)
                {
                    _waveOut.PlaybackStopped -= OnPlaybackStopped;
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = null;
                }

                _speedProvider = null;

                _sourceStream?.Dispose();
                _sourceStream = null;

                _pcmStream?.Dispose();
                _pcmStream = null;
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Stop();
        }
    }
}
