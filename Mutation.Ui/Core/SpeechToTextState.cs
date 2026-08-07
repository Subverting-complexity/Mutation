using CognitiveSupport;
using System;
using System.Threading;

namespace Mutation.Ui;

internal class SpeechToTextState : IDisposable
{
	internal SemaphoreSlim AudioRecorderLock { get; } = new SemaphoreSlim(1, 1);

	private readonly object _ctsLock = new();
	private CancellationTokenSource? _cts;

	private Func<IAudioRecorder?> GetAudioRecorder;
	internal bool RecordingAudio => GetAudioRecorder() != null;

	internal bool TranscribingAudio
	{
		get { lock (_ctsLock) return _cts is not null; }
	}

	// Whether a cancel has already been asked for and the transcription is winding down.
	// CancelTranscription deliberately leaves the source in place, so this stays true
	// until EndTranscription runs. A repeat press is then answered with "already
	// stopping" instead of the request line a second time (issue #299).
	internal bool CancellationRequested
	{
		get { lock (_ctsLock) return _cts is not null && _cts.IsCancellationRequested; }
	}

	public SpeechToTextState(
		Func<IAudioRecorder?> getAudioRecorder)
	{
		GetAudioRecorder = getAudioRecorder ?? throw new ArgumentNullException(nameof(getAudioRecorder));
	}

	// Returns a token tied to BOTH the new internal CTS and `external`.
	// Caller MUST use the returned token rather than re-reading state to avoid
	// a race with CancelTranscription on a different thread.
	internal CancellationToken StartTranscription(CancellationToken external = default)
	{
		lock (_ctsLock)
		{
			// A prior transcription should already have been ended, but if a stale
			// source lingers, cancel it before disposing so any operation still tied
			// to it unwinds as a cancellation rather than being abandoned mid-flight.
			if (_cts is not null)
			{
				try { _cts.Cancel(); } catch (ObjectDisposedException) { }
				_cts.Dispose();
			}
			_cts = CancellationTokenSource.CreateLinkedTokenSource(external);
			return _cts.Token;
		}
	}

	// Signals cancellation WITHOUT disposing the source. Called from the UI
	// cancel path, which can run while the owning transcription is still
	// retrying — a retry attempt links a fresh token to this source on every
	// try, so disposing here would surface ObjectDisposedException instead of a
	// clean cancellation. Disposal is deferred to EndTranscription.
	internal void CancelTranscription()
	{
		lock (_ctsLock)
		{
			if (_cts is null)
				return;
			try { _cts.Cancel(); } catch (ObjectDisposedException) { }
		}
	}

	// Ends the current transcription and disposes its source. Called by the
	// owning task once its awaited transcription (and every retry) has
	// finished, so nothing can still link a token to the source. Idempotent.
	internal void EndTranscription()
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
		EndTranscription();
		AudioRecorderLock.Dispose();
	}
}
