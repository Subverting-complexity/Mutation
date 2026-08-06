using System;
using NAudio.Wave;

namespace CognitiveSupport;

/// <summary>
/// The microphone capture contract <see cref="AudioRecorder"/> depends on. Extracting it from
/// NAudio's <c>WaveInEvent</c> (which needs a physical capture device) lets the recorder's
/// stop-and-drain behaviour be exercised in a unit test.
/// </summary>
public interface IAudioCaptureSource : IDisposable
{
	/// <summary>Raised for each buffer of captured 16-bit PCM.</summary>
	event EventHandler<WaveInEventArgs>? DataAvailable;

	/// <summary>
	/// Raised once, after the last <see cref="DataAvailable"/> buffer has been delivered.
	/// <see cref="AudioRecorder.StopRecording"/> waits for this before closing the encoder, so
	/// an implementation must raise it from a thread that does not need the caller of
	/// <see cref="StopRecording"/> to make progress first.
	/// </summary>
	event EventHandler<StoppedEventArgs>? RecordingStopped;

	void StartRecording();

	/// <summary>
	/// Signals the capture thread to stop. Buffers already filled may still be delivered
	/// afterwards; <see cref="RecordingStopped"/> marks the point where they have all arrived.
	/// </summary>
	void StopRecording();
}
