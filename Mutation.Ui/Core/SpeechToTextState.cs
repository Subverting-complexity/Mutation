using CognitiveSupport;
using System;
using System.Threading;

namespace Mutation.Ui;

internal class SpeechToTextState : IDisposable
{
	internal SemaphoreSlim AudioRecorderLock { get; } = new SemaphoreSlim(1, 1);

	private readonly object _ctsLock = new();
	private CancellationTokenSource? _cts;

	private Func<AudioRecorder> GetAudioRecorder;
	internal bool RecordingAudio => GetAudioRecorder() != null;

	internal bool TranscribingAudio
	{
		get { lock (_ctsLock) return _cts is not null; }
	}

	public SpeechToTextState(
		Func<AudioRecorder> getAudioRecorder)
	{
		GetAudioRecorder = getAudioRecorder ?? throw new ArgumentNullException(nameof(getAudioRecorder));
	}

	// Returns a token tied to BOTH the new internal CTS and `external`.
	// Caller MUST use the returned token rather than re-reading state to avoid
	// a race with StopTranscription on a different thread.
	internal CancellationToken StartTranscription(CancellationToken external = default)
	{
		lock (_ctsLock)
		{
			_cts?.Dispose();
			_cts = CancellationTokenSource.CreateLinkedTokenSource(external);
			return _cts.Token;
		}
	}

	internal void StopTranscription()
	{
		CancellationTokenSource? toDispose;
		lock (_ctsLock)
		{
			toDispose = _cts;
			_cts = null;
		}
		if (toDispose is null)
			return;
		try { toDispose.Cancel(); } catch (ObjectDisposedException) { }
		toDispose.Dispose();
	}

	public void Dispose()
	{
		StopTranscription();
		AudioRecorderLock.Dispose();
	}
}
