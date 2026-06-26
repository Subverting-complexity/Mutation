using NAudio.Wave;
using SoundTouch;

namespace CognitiveSupport;

/// <summary>
/// An NAudio sample provider that changes playback tempo while preserving pitch,
/// using the managed SoundTouch.Net time-stretch engine. It sits between a source
/// of audio samples and the output device: samples are pushed into SoundTouch,
/// stretched or compressed in time, and pulled back out at the requested rate.
///
/// The sample rate and channel count are unchanged, so a voice keeps its natural
/// pitch — only how fast it plays changes. <see cref="Tempo"/> can be changed at
/// any time, including mid-playback, and takes effect on the audio produced next.
/// </summary>
public sealed class SoundTouchSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly SoundTouchProcessor _processor;
    private readonly float[] _sourceBuffer;
    private readonly int _channels;
    private readonly object _lock = new();
    private bool _sourceExhausted;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public SoundTouchSampleProvider(ISampleProvider source, int readFrames = 4096)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _channels = source.WaveFormat.Channels;
        _processor = new SoundTouchProcessor
        {
            SampleRate = source.WaveFormat.SampleRate,
            Channels = _channels,
        };
        _sourceBuffer = new float[readFrames * _channels];
    }

    /// <summary>
    /// The tempo multiplier: 1.0 is normal speed, 2.0 is twice as fast, 0.5 is
    /// half speed. Safe to set from another thread while audio is playing.
    /// </summary>
    public double Tempo
    {
        get { lock (_lock) { return _processor.Tempo; } }
        set { lock (_lock) { _processor.Tempo = value; } }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            int framesRequested = count / _channels;
            int framesWritten = 0;

            while (framesWritten < framesRequested)
            {
                int framesRemaining = framesRequested - framesWritten;
                var output = new Span<float>(buffer, offset + (framesWritten * _channels), framesRemaining * _channels);
                int received = _processor.ReceiveSamples(output, framesRemaining);
                if (received > 0)
                {
                    framesWritten += received;
                    continue;
                }

                if (_sourceExhausted)
                    break;

                int floatsRead = _source.Read(_sourceBuffer, 0, _sourceBuffer.Length);
                if (floatsRead == 0)
                {
                    // No more input: flush SoundTouch so any audio still buffered
                    // inside the time-stretch stage drains out on the next receive.
                    _sourceExhausted = true;
                    _processor.Flush();
                }
                else
                {
                    _processor.PutSamples(new ReadOnlySpan<float>(_sourceBuffer, 0, floatsRead), floatsRead / _channels);
                }
            }

            return framesWritten * _channels;
        }
    }
}
