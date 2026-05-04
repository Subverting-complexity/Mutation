using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;

namespace CognitiveSupport;

/// <summary>
/// Audio player that supports OGG/Opus files using NAudio and Concentus.
/// This bypasses Windows Media Foundation which doesn't natively support OGG/Opus.
/// </summary>
public class AudioPlayer : IDisposable
{
    private WaveOutEvent? _waveOut;
    private OpusDecoder? _decoder;
    private MemoryStream? _pcmStream;
    private RawSourceWaveStream? _waveStream;
    private WaveStream? _readerStream;
    private WaveStream? _conversionStream;
    private readonly object _playLock = new();
    private bool _disposed;

    // Opus parameters matching AudioRecorder
    private const int SampleRate = 48000;
    private const int Channels = 1;

    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

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
    /// Plays an OGG/Opus file by decoding it with Concentus and playing via NAudio.
    /// </summary>
    private void PlayOgg(string filePath)
    {
        // Read and decode the entire OGG file to PCM
        using var fileStream = File.OpenRead(filePath);
        var oggReader = new OpusOggReadStream(_decoder = new OpusDecoder(SampleRate, Channels), fileStream);

        // Collect all decoded samples
        var allSamples = new List<short>();
        while (oggReader.HasNextPacket)
        {
            var samples = oggReader.DecodeNextPacket();
            if (samples != null && samples.Length > 0)
            {
                allSamples.AddRange(samples);
            }
        }

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
        _waveStream = new RawSourceWaveStream(_pcmStream, waveFormat);

        _waveOut = new WaveOutEvent();
        _waveOut.PlaybackStopped += OnPlaybackStopped;
        _waveOut.Init(_waveStream);
        _waveOut.Play();
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
        _readerStream = reader;

        _waveOut = new WaveOutEvent();
        _waveOut.PlaybackStopped += OnPlaybackStopped;

        // If the format isn't PCM, we need to convert it
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm)
        {
            var pcmStream = WaveFormatConversionStream.CreatePcmStream(reader);
            _conversionStream = pcmStream;
            _waveOut.Init(pcmStream);
        }
        else
        {
            _waveOut.Init(reader);
        }

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

                _waveStream?.Dispose();
                _waveStream = null;

                _conversionStream?.Dispose();
                _conversionStream = null;

                _readerStream?.Dispose();
                _readerStream = null;

                _pcmStream?.Dispose();
                _pcmStream = null;

                _decoder = null;
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
