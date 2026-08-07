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
    private readonly Action<string>? _beforePrepare;
    private IAudioOutputDevice? _waveOut;
    private MemoryStream? _pcmStream;
    private WaveStream? _sourceStream;
    private SoundTouchSampleProvider? _speedProvider;
    private readonly object _playLock = new();
    private double _speed = PlaybackSpeedOptions.Default;
    private bool _disposed;

    // Bumped by every request to play and by every Stop. A decode that finishes holding a
    // stale number has been superseded — the user pressed Stop, or asked for a different
    // file — and throws its work away instead of starting audio nobody is waiting for any
    // more. Only meaningful once decoding moved off the calling thread and a request could
    // outlive the answer.
    private int _playGeneration;

    // Opus parameters matching AudioRecorder
    private const int SampleRate = 48000;
    private const int Channels = 1;

    public AudioPlayer()
        : this(() => new WaveOutAudioOutputDevice())
    {
    }

    /// <param name="beforePrepare">
    /// Test seam. Runs on the decoding thread, before the file is opened, so a test can hold a
    /// decode open and act while it is in flight — which is the only way to reach the
    /// superseded and failed-decode paths deterministically rather than by racing the thread
    /// pool. Null in production.
    /// </param>
    internal AudioPlayer(Func<IAudioOutputDevice> outputDeviceFactory, Action<string>? beforePrepare = null)
    {
        _outputDeviceFactory = outputDeviceFactory ?? throw new ArgumentNullException(nameof(outputDeviceFactory));
        _beforePrepare = beforePrepare;
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
    /// Plays an audio file, decoding it on the thread pool. Supports OGG/Opus, WAV, and MP3.
    /// A whole-file Opus decode takes seconds to tens of seconds for a long recording, and on
    /// the UI thread that is a frozen window and a silent screen reader for the duration
    /// (issue #281).
    /// <para>
    /// Only the decode moves. The output device is still constructed where the caller was,
    /// because NAudio's <c>WaveOutEvent</c> captures <c>SynchronizationContext.Current</c> at
    /// construction and posts <c>PlaybackStopped</c> back to it — build it on a pool thread
    /// with no context and <see cref="PlaybackEnded"/> would start arriving on NAudio's own
    /// playback thread, where its listeners touch the UI. Starting is microseconds of work;
    /// decoding is the part that had to move.
    /// </para>
    /// </summary>
    public async Task PlayAsync(string filePath)
    {
        int generation = NextGeneration();
        PreparedSource? prepared = null;
        try
        {
            // Reading and decoding happen outside the lock, so a decode never blocks a Speed
            // change. ConfigureAwait(true) is the point of the method, not an oversight: the
            // continuation has to resume where the caller was so the device is built there.
            prepared = await Task.Run(() => Prepare(filePath)).ConfigureAwait(true);
            Launch(prepared, generation);
        }
        catch (Exception ex)
        {
            // A superseded request stays quiet and touches nothing but its own work. The
            // player belongs to a newer file by now, and Stop here would cut off audio that is
            // already playing while PlaybackFailed pops an error dialog about a file the user
            // moved on from — the failing decode is seconds behind the request that replaced
            // it.
            if (!IsCurrent(generation))
            {
                prepared?.Dispose();
                return;
            }

            // Stop first, then release the buffers. StartPlayback can throw after it has
            // published the source and attached the output device, and disposing the stream
            // the device is reading from before detaching the device is the wrong order.
            Stop();
            prepared?.Dispose();
            PlaybackFailed?.Invoke(this, $"Playback failed: {ex.Message}");
        }
    }

    private int NextGeneration()
    {
        lock (_playLock)
        {
            return ++_playGeneration;
        }
    }

    /// <summary>Whether this request is still the one the player is answering.</summary>
    private bool IsCurrent(int generation)
    {
        lock (_playLock)
        {
            return !_disposed && generation == _playGeneration;
        }
    }

    /// <summary>
    /// Publishes a decoded source and starts it, unless something newer has happened since the
    /// decode began. A null source means the file held no audio.
    /// </summary>
    private void Launch(PreparedSource? prepared, int generation)
    {
        bool nothingToPlay;

        lock (_playLock)
        {
            // Checked before anything is touched: a stale decode owns nothing here, so it
            // neither tears the current playback down nor reports anything about itself.
            if (_disposed || generation != _playGeneration)
            {
                prepared?.Dispose();
                return;
            }

            nothingToPlay = prepared is null;
            if (!nothingToPlay)
            {
                // Stopping and publishing happen under one lock, so two overlapping requests
                // cannot leave the loser's output device attached with nothing to release it.
                StopCore();

                _pcmStream = prepared!.Pcm;
                StartPlayback(prepared.Stream);
            }
        }

        // Raised outside the lock: it runs listener code, which is free to call straight back
        // into PlayAsync or Stop.
        if (nothingToPlay)
            PlaybackFailed?.Invoke(this, "No audio data found in file.");
    }

    /// <summary>
    /// Opens or decodes the file into a source ready to play, without touching any of the
    /// player's own state. Returns null when the file holds no audio.
    /// </summary>
    private PreparedSource? Prepare(string filePath)
    {
        _beforePrepare?.Invoke(filePath);

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
            // Supersede any decode still running, so a file the user has already stopped
            // does not start playing the moment its decode lands.
            _playGeneration++;
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
