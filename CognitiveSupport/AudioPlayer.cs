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
    private readonly Func<IAudioOutputDevice> _outputDeviceFactory;
    private IAudioOutputDevice? _waveOut;
    private MemoryStream? _pcmStream;
    private WaveStream? _sourceStream;
    private SoundTouchSampleProvider? _speedProvider;
    private readonly object _playLock = new();
    private double _speed = PlaybackSpeedOptions.Default;
    private bool _disposed;

    // Opus parameters matching AudioRecorder
    private const int SampleRate = 48000;
    private const int Channels = 1;

    public AudioPlayer()
        : this(() => new WaveOutAudioOutputDevice())
    {
    }

    internal AudioPlayer(Func<IAudioOutputDevice> outputDeviceFactory)
    {
        _outputDeviceFactory = outputDeviceFactory ?? throw new ArgumentNullException(nameof(outputDeviceFactory));
    }

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
        PreparedSource? prepared = null;
        try
        {
            // Reading and decoding happen outside the lock. Decoding a long recording takes
            // seconds, and doing it under _playLock blocked every Speed change made from the
            // UI thread for that whole window, freezing the window.
            prepared = Prepare(filePath);
            if (prepared is null)
            {
                PlaybackFailed?.Invoke(this, "No audio data found in file.");
                return;
            }

            lock (_playLock)
            {
                // Stopping and publishing happen under one lock, so two overlapping Play calls
                // cannot leave the loser's output device attached with nothing to release it.
                StopCore();

                if (_disposed)
                {
                    prepared.Dispose();
                    return;
                }

                _pcmStream = prepared.Pcm;
                StartPlayback(prepared.Stream);
            }
        }
        catch (Exception ex)
        {
            Stop();
            prepared?.Dispose();
            PlaybackFailed?.Invoke(this, $"Playback failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens or decodes the file into a source ready to play, without touching any of the
    /// player's own state. Returns null when the file holds no audio.
    /// </summary>
    private static PreparedSource? Prepare(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Ogg/Opus is decoded in managed code: Windows Media Foundation has no demuxer for it,
        // so files this app did not record itself — WhatsApp voice notes and the like — would
        // otherwise not open at all. The decoder is shared with the import path.
        if (extension == ".ogg" || extension == ".opus")
        {
            MemoryStream pcm = OggOpusPcmDecoder.DecodeToPcmStream(filePath);
            if (pcm.Length == 0)
            {
                pcm.Dispose();
                return null;
            }

            var waveFormat = new WaveFormat(SampleRate, 16, Channels);
            return new PreparedSource(new RawSourceWaveStream(pcm, waveFormat), pcm);
        }

        WaveStream reader = extension switch
        {
            ".wav" => new WaveFileReader(filePath),
            ".mp3" => new Mp3FileReader(filePath),
            _ => new AudioFileReader(filePath) // Generic reader for other formats
        };

        return new PreparedSource(reader, null);
    }

    /// <summary>
    /// A decoded source together with the backing buffer that has to outlive it —
    /// <c>RawSourceWaveStream</c> does not dispose the stream it reads from. Built outside the
    /// playback lock, then either published to the player's fields or thrown away whole.
    /// </summary>
    private sealed record PreparedSource(WaveStream Stream, MemoryStream? Pcm) : IDisposable
    {
        public void Dispose()
        {
            try { Stream.Dispose(); } catch { /* Already torn down by Stop. */ }
            try { Pcm?.Dispose(); } catch { /* Already torn down by Stop. */ }
        }
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

        _waveOut = _outputDeviceFactory();
        _waveOut.PlaybackStopped += OnPlaybackStopped;
        _waveOut.Init(output);
        _waveOut.Play();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // A device that is no longer the current one has nothing to say: a newer Play already
        // took over and disposed it on its way in. Checked before the events are raised, so a
        // stopped notification cannot arrive after the next file has started and have a
        // listener tear that one down instead.
        lock (_playLock)
        {
            if (!ReferenceEquals(sender, _waveOut))
                return;
        }

        // Raised outside the lock: these run listener code, which is free to call back into
        // Play or Stop, and holding the lock across it invites a stall.
        if (e.Exception != null)
        {
            PlaybackFailed?.Invoke(this, $"Playback error: {e.Exception.Message}");
        }
        else
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }

        // Release the output device and the decoded audio now that the file has run to its
        // end. Waiting for the next Stop/Dispose meant a recording played to completion kept
        // an open WinMM output handle and its whole PCM buffer resident for the rest of the
        // app's life. StopCore detaches this handler first, so this cannot recurse.
        lock (_playLock)
        {
            // Re-checked: a listener above may have started the next file, and that instance
            // owns the teardown now.
            if (!ReferenceEquals(sender, _waveOut))
                return;

            StopCore();
        }
    }

    public void Stop()
    {
        lock (_playLock)
        {
            StopCore();
        }
    }

    /// <summary>
    /// Tears down the current playback chain. Callers must already hold <c>_playLock</c>.
    /// </summary>
    private void StopCore()
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

    public void Dispose()
    {
        lock (_playLock)
        {
            if (_disposed)
                return;

            // Set under the lock so a Play that is still decoding sees it and throws its
            // prepared source away instead of attaching it to a disposed player.
            _disposed = true;
            StopCore();
        }
    }
}
