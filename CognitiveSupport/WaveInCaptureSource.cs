using System;
using System.Threading;
using NAudio.Wave;

namespace CognitiveSupport;

/// <summary>
/// The real microphone: <see cref="IAudioCaptureSource"/> backed by NAudio's
/// <c>WaveInEvent</c>.
/// </summary>
internal sealed class WaveInCaptureSource : IAudioCaptureSource
{
	private readonly WaveInEvent _waveIn;

	public WaveInCaptureSource(int deviceNumber, WaveFormat waveFormat, int bufferMilliseconds)
	{
		// WaveInEvent captures SynchronizationContext.Current in its constructor and posts
		// RecordingStopped to it. Constructed on the UI thread, the event would therefore be
		// delivered on the UI thread — and AudioRecorder.StopRecording, called from that same
		// thread, waits for it, which would deadlock until the wait times out. Building it
		// with no context keeps the event on NAudio's own recording thread, where it is raised
		// straight after the final buffer.
		SynchronizationContext? previous = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext(null);
		try
		{
			_waveIn = new WaveInEvent
			{
				DeviceNumber = deviceNumber,
				WaveFormat = waveFormat,
				BufferMilliseconds = bufferMilliseconds
			};
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(previous);
		}

		_waveIn.DataAvailable += (_, e) => DataAvailable?.Invoke(this, e);
		_waveIn.RecordingStopped += (_, e) => RecordingStopped?.Invoke(this, e);
	}

	public event EventHandler<WaveInEventArgs>? DataAvailable;

	public event EventHandler<StoppedEventArgs>? RecordingStopped;

	public void StartRecording() => _waveIn.StartRecording();

	public void StopRecording() => _waveIn.StopRecording();

	public void Dispose() => _waveIn.Dispose();
}
