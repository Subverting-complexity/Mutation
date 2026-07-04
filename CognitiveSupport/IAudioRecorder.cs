using System;

namespace CognitiveSupport;

/// <summary>
/// The recording contract <c>SpeechToTextManager</c> depends on. Extracting it
/// from the concrete <see cref="AudioRecorder"/> (which drives real NAudio
/// hardware) lets the manager be unit-tested in isolation with a recorder that
/// can fail a start on demand.
/// </summary>
public interface IAudioRecorder : IDisposable
{
	/// <summary>
	/// After <see cref="StopRecording"/>, the trimmed speech duration in seconds
	/// when silence stripping was active; null when stripping was disabled.
	/// </summary>
	double? TrimmedSpeechSeconds { get; }

	/// <summary>
	/// First exception (if any) that occurred while encoding audio on the capture
	/// thread, captured there rather than thrown so it does not terminate the
	/// process. Callers surface it after <see cref="StopRecording"/>.
	/// </summary>
	Exception? CaptureException { get; }

	void StartRecording(int captureDeviceIndex, string outputFile, SilenceTrimmerOptions? silenceOptions = null);

	void StopRecording();
}
