using CognitiveSupport;
using Microsoft.UI.Dispatching;
using Mutation.Ui.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Mutation.Ui.Core;

/// <summary>
/// Carries the raw transcript and optional pre-formatted text so that
/// downstream handlers can avoid re-applying rules to LLM output.
/// </summary>
/// <param name="FastModeNotice">
/// Set when this transcript's prompt asked for Fast mode, could not have it, and the
/// user has not already been told why this session. It travels with the transcript
/// rather than on the status channel because the handler that delivers the transcript
/// is what announces last — the status channel supersedes rather than queues, so a
/// separate announcement would be talked over by "Transcript ready."
/// </param>
/// <param name="LlmCancelled">
/// Set when the language-model step was cancelled but the dictation survived it. Travels
/// with the transcript for exactly the reason <paramref name="FastModeNotice"/> does: the
/// handler that delivers the transcript raises the last status of the run, so a cancel
/// announced from this side is superseded by that delivery a moment later and heard by
/// nobody.
/// </param>
public record TranscriptResult(
	string RawText,
	string? FormattedText = null,
	FastModeFallbackReason? FastModeNotice = null,
	bool LlmCancelled = false);

public class AudioSessionManager : IDisposable
{
    private readonly SpeechToTextManager _speechManager;
    private readonly AudioDeviceManager _audioDeviceManager;
    private readonly TranscriptFormatter _transcriptFormatter;
    private readonly Settings _settings;
    private readonly MicrophoneLevelWriteCoordinator _levelWriteCoordinator;
    private readonly FastModeNoticeTracker _fastModeNotices;
    private readonly AudioPlayer _playbackPlayer;
    private readonly LlmOperationState _llmOperation = new();
    private SpeechSession? _playingSession;
    private SpeechSession? _selectedSession;
    private bool _currentRecordingUsesLlmProcessing;

    public ObservableCollection<SpeechSession> SessionHistory { get; } = new();

    public SpeechSession? SelectedSession
    {
        get => _selectedSession;
        private set
        {
            if (_selectedSession != value)
            {
                _selectedSession = value;
                SelectedSessionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Whether a recording is playing <em>or on its way there</em>. Decoding happens off the
    /// UI thread now, so there are seconds between the user pressing Play and
    /// <see cref="IsPlaying"/> becoming true — and for that whole window the session is
    /// committed and the Play button is a Stop button. Anything deciding what to enable
    /// during playback wants this, not <see cref="IsPlaying"/>, or it leaves controls live
    /// for exactly the stretch this feature exists to make usable.
    /// </summary>
    public bool IsPlaybackActive => _playingSession != null;
    public bool IsRecording => _speechManager.Recording;
    public bool IsTranscribing => _speechManager.Transcribing;

    /// <summary>
    /// Whether the language model is still working on a delivered transcript. Its own
    /// flag because the recorder's is already false by then — the transcription ends and
    /// disposes its cancellation source before the prompt is sent — so without this the
    /// dictation hotkey read the LLM step as "nothing in flight" and opened a fresh
    /// recording on top of it, which then had its state clobbered when the model replied
    /// (issue #256).
    /// </summary>
    public bool IsProcessingWithLlm => _llmOperation.Running;

    // The recorder's own view of what it is doing. Used only where a failure has left
    // the session in a state this class cannot name from where it stands — everywhere
    // else the raising code states the activity it is entering, so a change that is
    // still in flight cannot be announced as its opposite.
    private RecordingActivity CurrentActivity =>
        IsRecording ? RecordingActivity.Recording
        : IsTranscribing ? RecordingActivity.Transcribing
        : RecordingActivity.Idle;

    /// <summary>
    /// Playback speed for the recorded-audio player (1.0 = normal). Setting it
    /// retunes audio that is already playing, without restarting. The value is
    /// snapped to the nearest supported speed by the player.
    /// </summary>
    public double PlaybackSpeed
    {
        get => _playbackPlayer.Speed;
        set => _playbackPlayer.Speed = value;
    }

    public event EventHandler? SelectedSessionChanged;
    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackStopped;
    /// <summary>
    /// Raised with the activity the session is entering. The activity is carried on the
    /// event rather than read back off <see cref="IsRecording"/> by the handler: the
    /// handler runs on the UI thread via the dispatcher, so by then the recorder's flags
    /// may not have caught up, and a stop could be announced as a start (issue #271).
    /// </summary>
    public event EventHandler<RecordingActivity>? StateChanged;

    public event EventHandler<TranscriptResult>? TranscriptReady;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string>? StatusMessage;

    public AudioSessionManager(
        SpeechToTextManager speechManager,
        AudioDeviceManager audioDeviceManager,
        TranscriptFormatter transcriptFormatter,
        Settings settings,
        MicrophoneLevelWriteCoordinator levelWriteCoordinator,
        FastModeNoticeTracker fastModeNotices)
    {
        _speechManager = speechManager;
        _audioDeviceManager = audioDeviceManager;
        _transcriptFormatter = transcriptFormatter;
        _settings = settings;
        _levelWriteCoordinator = levelWriteCoordinator;
        _fastModeNotices = fastModeNotices;

        _playbackPlayer = new AudioPlayer();
        _playbackPlayer.PlaybackEnded += PlaybackPlayer_PlaybackEnded;
        _playbackPlayer.PlaybackFailed += PlaybackPlayer_PlaybackFailed;
    }

    public void RefreshSessions(SpeechSession? preferredSelection = null, string? preferredPath = null)
    {
        var snapshot = _speechManager.GetSessions();
        SessionHistory.Clear();
        foreach (var session in snapshot)
        {
            SessionHistory.Add(session);
        }

        string? path = preferredPath;
        if (preferredSelection != null)
        {
            path = preferredSelection.FilePath;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            SelectedSession = SessionHistory.FirstOrDefault(s => PathsEqual(s.FilePath, path));
        }
        
        if (SelectedSession == null && SessionHistory.Count > 0)
        {
            SelectedSession = SessionHistory.FirstOrDefault();
        }
        else if (SessionHistory.Count == 0)
        {
            SelectedSession = null;
        }
    }

    public async Task NavigateSessionsAsync(int direction)
    {
        // IsProcessingWithLlm included: stepping to another session during the model call
        // would move the selection out from under the transcript it is about to deliver.
        // Answered rather than returning in silence — the buttons are disabled for this
        // stretch, but a caller that gets here anyway deserves a reason.
        if (IsRecording || IsTranscribing || IsProcessingWithLlm)
        {
            StatusMessage?.Invoke(this, "Finish the current operation before changing sessions.");
            return;
        }

        RefreshSessions(preferredSelection: SelectedSession);

        if (SessionHistory.Count == 0)
            return;

        int currentIndex = SelectedSession != null ? SessionHistory.IndexOf(SelectedSession) : -1;
        if (currentIndex < 0)
            currentIndex = 0;

        int targetIndex = direction < 0 ? currentIndex - 1 : currentIndex + 1;
        if (targetIndex < 0 || targetIndex >= SessionHistory.Count)
            return;

        var targetSession = SessionHistory[targetIndex];

        StopPlayback();
        SelectedSession = targetSession;
        await PlaySelectedSessionAsync();
    }

    public async Task StartStopRecordingAsync(ISpeechToTextService activeService, bool useLlmProcessing, string prompt, LlmSettings.LlmPrompt? llmPrompt = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // What this press means is decided in one place, by DictationPressPlanner, so
            // the two orderings that matter — the model call ahead of the recorder, and a
            // repeat press answered differently from the first — can be asserted without a
            // window. See that class for why each ordering is what it is.
            var press = DictationPressPlanner.For(
                isTranscribing: IsTranscribing,
                transcriptionCancelRequested: _speechManager.TranscriptionCancelRequested,
                isProcessingWithLlm: IsProcessingWithLlm,
                llmCancelRequested: _llmOperation.CancelRequested,
                isRecording: IsRecording);

            // None of the four cancel answers raises a state change. Nothing has changed
            // yet — only a cancellation has been asked for — and the operation that owns
            // the state raises its own from its finally when it unwinds. Raising one here
            // would race that: both are delivered through the dispatcher, and if the
            // unwind's landed first the window would settle on a stale busy state with
            // nothing left to correct it, leaving the buttons disabled and the transcript
            // box read-only for the rest of the session.
            //
            // Each says progress, never completion: the operation announces its own
            // "cancelled" when it lets go, and saying that here too gave a screen-reader
            // user the same sentence twice (issue #299).
            switch (press)
            {
                case DictationPressAction.CancelTranscription:
                    _speechManager.CancelTranscription();
                    BeepPlayer.Play(BeepType.Failure);
                    StatusMessage?.Invoke(this, CancellationMessages.TranscriptionRequested);
                    return;

                case DictationPressAction.CancelLlmProcessing:
                    _llmOperation.Cancel();
                    BeepPlayer.Play(BeepType.Failure);
                    StatusMessage?.Invoke(this, CancellationMessages.LlmRequested);
                    return;

                // A repeat press on a stop already asked for. Answered rather than ignored:
                // a shortcut that produces silence reads as one that did not register. No
                // second beep — the first press already gave the audible acknowledgement,
                // and the stop it refers to has not changed.
                case DictationPressAction.TranscriptionAlreadyStopping:
                case DictationPressAction.LlmAlreadyStopping:
                    StatusMessage?.Invoke(this, CancellationMessages.AlreadyStopping);
                    return;
            }

            if (press == DictationPressAction.StartRecording)
            {
                _currentRecordingUsesLlmProcessing = useLlmProcessing;
                StopPlayback();

                // Re-assert the pinned capture level right before recording so a level
                // another app may have changed is corrected back to the user's choice.
                // The write runs on the shared level-write worker, off the UI thread, so
                // the failure path's device re-enumeration cannot freeze the UI (and the
                // screen reader) — the same off-thread guarantee #171 gave the mute
                // toggle. We await it so the level is settled before capture starts.
                // If it cannot be applied and verified, tell the user rather than
                // recording silently at the wrong gain — the recording still proceeds
                // so no audio is lost, but the failure is signalled with a beep (played
                // before capture starts, so it is not recorded) and a persistent status
                // message that replaces the usual "Listening" line.
                var levelResult = await ReassertPinnedLevelOffThreadAsync();
                if (levelResult.Failed)
                    BeepPlayer.Play(BeepType.Failure);

                // Announce "Listening" only after the recorder has actually
                // started; announcing earlier would tell a screen-reader user
                // the microphone is live while a lingering transcription still
                // holds the recorder lock and audio is being lost.
                var session = await _speechManager.StartRecordingAsync(_audioDeviceManager.MicrophoneDeviceIndex);
                StatusMessage?.Invoke(this, levelResult.Failed
                    ? "Listening — but the pinned microphone level could not be applied; recording at the current level."
                    : "Listening for audio...");
                RefreshSessions(session);
                StateChanged?.Invoke(this, RecordingActivity.Recording);
            }
            else
            {
                _currentRecordingUsesLlmProcessing = useLlmProcessing;
                StopPlayback();
                StatusMessage?.Invoke(this, "Transcribing your recording...");
                // Transcribing, stated outright. The recorder has not been asked to stop
                // yet, so a handler reading IsRecording here would see a live recording
                // and greet a stop with the start beep and "Recording..." (issue #271).
                StateChanged?.Invoke(this, RecordingActivity.Transcribing);

                bool cancelled = false;
                try
                {
                    // The end beep is raised from inside the stop, not from here: it has to
                    // land after the recorder is closed (or it is captured into the very
                    // recording it ends) and before the transcription request (or the user
                    // waits out a network call for the sound that says capture stopped).
                    string text = await _speechManager.StopRecordingAndTranscribeAsync(
                        activeService,
                        prompt,
                        cancellationToken,
                        onRecordingStopped: () => BeepPlayer.Play(BeepType.End));
                    await ProcessTranscriptAsync(text, llmPrompt, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    StatusMessage?.Invoke(this, CancellationMessages.TranscriptionCompleted);
                }
                catch (NoSpeechDetectedException)
                {
                    BeepPlayer.Play(BeepType.Failure);
                    StatusMessage?.Invoke(this, "No speech detected — nothing to transcribe.");
                }
                finally
                {
                    // Cancelled rather than Idle when nothing was delivered, so the
                    // "Transcribing..." placeholder is cleared instead of left standing
                    // (issue #295). Still raised from the finally, and still exactly one
                    // state change, so the ordering guarantees above are untouched.
                    StateChanged?.Invoke(this, cancelled ? RecordingActivity.Cancelled : RecordingActivity.Idle);
                }
            }
        }
        catch (RecorderBusyException)
        {
            // Busy means a *different* operation owns the recorder and the session state.
            // This one changed nothing, so it raises nothing: the owner will announce its
            // own Idle when it finishes, and a state change from here would race it.
            BeepPlayer.Play(BeepType.Failure);
            StatusMessage?.Invoke(this, "Still finishing the previous operation — try again shortly.");
        }
        catch (Exception ex)
        {
            // Any other failure belongs to this operation — the busy case, the only one
            // where another operation could be running concurrently, is caught above — so
            // there is no competing raise to race with and the recorder's own flags are
            // the best account of where the failure left things.
            ErrorOccurred?.Invoke(this, ex.Message);
            StateChanged?.Invoke(this, CurrentActivity);
        }
    }

    // Re-asserts the pinned capture level through the shared off-thread write worker.
    // A null pin means pinning is disabled — nothing to write, so the level is
    // Unchanged. A superseded write (the coordinator dropped this request because a
    // newer level was requested and applied instead) is also Unchanged for signalling
    // purposes: the device settled on the newer value, so there is nothing to warn the
    // user about here.
    private async Task<CaptureLevelResult> ReassertPinnedLevelOffThreadAsync()
    {
        if (_settings.AudioSettings?.PinnedCaptureLevel is not int pinned)
            return new CaptureLevelResult(CaptureLevelOutcome.Unchanged);

        return await _levelWriteCoordinator.RequestLatestAsync(pinned)
            ?? new CaptureLevelResult(CaptureLevelOutcome.Unchanged);
    }

    public async Task RetryTranscriptionAsync(ISpeechToTextService activeService, string prompt, CancellationToken cancellationToken = default)
    {
        // IsProcessingWithLlm included, or a retry launched during the model call raises
        // its own TranscriptReady and races the one that call is about to raise — the same
        // clobber the press planner closes, reached through a different door (issue #256).
        if (IsRecording || IsTranscribing || IsProcessingWithLlm)
        {
            StatusMessage?.Invoke(this, "Finish the current operation before retrying.");
            return;
        }

        if (SelectedSession == null)
        {
            StatusMessage?.Invoke(this, "No session available to retry.");
            return;
        }

        bool cancelled = false;
        try
        {
            StopPlayback();
            StatusMessage?.Invoke(this, "Transcribing your recording...");
            StateChanged?.Invoke(this, RecordingActivity.Transcribing);

            string text = await _speechManager.TranscribeExistingRecordingAsync(activeService, SelectedSession, prompt, cancellationToken);
            // Retry doesn't apply LLM processing — pass raw text only so
            // FinalizeTranscript applies rules-based formatting as usual.
            TranscriptReady?.Invoke(this, new TranscriptResult(text));
            StatusMessage?.Invoke(this, "Transcript refreshed from the selected session.");
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            StatusMessage?.Invoke(this, CancellationMessages.TranscriptionCompleted);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            StateChanged?.Invoke(this, cancelled ? RecordingActivity.Cancelled : RecordingActivity.Idle);
        }
    }

    public async Task ImportAudioAsync(StorageFile file, ISpeechToTextService activeService, string prompt, CancellationToken cancellationToken = default)
    {
        // IsProcessingWithLlm included, for the same reason as RetryTranscriptionAsync.
        if (IsRecording || IsTranscribing || IsProcessingWithLlm)
        {
            StatusMessage?.Invoke(this, "Finish the current operation before uploading.");
            return;
        }

        bool cancelled = false;
        try
        {
            StopPlayback();
            StatusMessage?.Invoke(this, $"Transcribing {file.Name}...");
            StateChanged?.Invoke(this, RecordingActivity.Transcribing);

            var session = await _speechManager.ImportUploadedAudioAsync(file.Path, cancellationToken);
            RefreshSessions(session);

            string text = await _speechManager.TranscribeExistingRecordingAsync(activeService, session, prompt, cancellationToken);
            TranscriptReady?.Invoke(this, new TranscriptResult(text));
            StatusMessage?.Invoke(this, $"Transcript generated from {session.FileName}.");
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            StatusMessage?.Invoke(this, CancellationMessages.TranscriptionCompleted);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            StateChanged?.Invoke(this, cancelled ? RecordingActivity.Cancelled : RecordingActivity.Idle);
        }
    }

    private async Task ProcessTranscriptAsync(string text, LlmSettings.LlmPrompt? llmPrompt, CancellationToken cancellationToken)
    {
        // Always run rules-based formatting first
        string rulesFormattedText = _transcriptFormatter.ApplyRules(text, false);
        string? llmProcessedText = null;
        FastModeFallback? fastModeFallback = null;
        bool llmCancelled = false;

        if (_currentRecordingUsesLlmProcessing && llmPrompt != null)
        {
            // Declared out here so the catch filters below can tell the three ways this
            // call can end in an OperationCanceledException apart from one another.
            CancellationToken llmToken = default;
            try
            {
                StatusMessage?.Invoke(this, "Processing with LLM...");
                string modelName = !string.IsNullOrWhiteSpace(llmPrompt.ModelName) ? llmPrompt.ModelName : LlmSettings.DefaultModel;
                ErrorLogger.LogInfo("LLM", $"LLM processing starting (model={modelName}, fastMode={llmPrompt.FastMode}).");
                var requestOptions = new LlmRequestOptions
                {
                    FastMode = llmPrompt.FastMode,
                    OnFastModeFallback = f => fastModeFallback = f,
                };
                // Claimed for the length of the call so a second press of the dictation
                // hotkey can cut it short, and released by the handle's Dispose — which
                // releases the slot only while this run still owns it.
                using var run = _llmOperation.Begin(cancellationToken);
                llmToken = run.Token;
                // Its own activity, so the window stops naming the step that has already
                // finished and the buttons that started this can offer to stop it. Raised
                // after the slot is claimed — the handler asks IsProcessingWithLlm to decide
                // what to enable, and it has to be true by the time that question is put.
                // Corrected by the finally in StartStopRecordingAsync, still the only place
                // a terminal state comes from.
                StateChanged?.Invoke(this, RecordingActivity.ProcessingWithLlm);
                // Pass the rules-formatted text to the LLM
                llmProcessedText = await _transcriptFormatter.ProcessWithLlmAsync(rulesFormattedText, llmPrompt.Content, modelName, requestOptions, run.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The window is closing. Deliver nothing: the delivery path pastes into
                // whatever application has focus, and by now that is not this one — the
                // user quit and would get text typed into whatever they moved on to.
                throw;
            }
            catch (OperationCanceledException) when (llmToken.IsCancellationRequested)
            {
                // The user asked for this, so it is not a failure. The dictation is not
                // thrown away with the model call: the rules-formatted transcript is
                // delivered exactly as it is when the model call fails, because losing a
                // long dictation is far worse than an untidied one.
                llmCancelled = true;
                ErrorLogger.LogInfo("LLM", "LLM processing cancelled; delivering the rules-formatted transcript.");
            }
            catch (Exception ex)
            {
                // A per-attempt timeout arrives here as an OperationCanceledException too,
                // once the ladder is exhausted — and nobody cancelled anything, so it is
                // reported as the failure it is. Reading it as the user's own cancel is how
                // a dead provider came to be announced as something they had asked for.
                ErrorLogger.LogError("LLM processing failed", ex);
                StatusMessage?.Invoke(this, $"LLM processing failed: {ex.Message}. Using rules-formatted transcript.");
                // llmProcessedText remains null — FinalizeTranscript will fall back to rules-only
            }
        }

        // Claimed here because this is the only side that knows which prompt ran; the
        // notice then rides on the transcript so it is announced with the delivery
        // outcome instead of competing with it.
        FastModeFallbackReason? fastModeNotice =
            fastModeFallback is not null && llmPrompt is not null
            && _fastModeNotices.ShouldAnnounce(llmPrompt.Id, fastModeFallback.Reason)
                ? fastModeFallback.Reason
                : null;

        // Pass raw text and the final text separately so that
        // FinalizeTranscript does not re-apply rules to LLM output. The cancel rides along
        // rather than being announced here, for the same reason the Fast mode notice does.
        TranscriptReady?.Invoke(this, new TranscriptResult(text, llmProcessedText ?? rulesFormattedText, fastModeNotice, llmCancelled));
        // No delivery announcement from here. The handler that delivers the transcript
        // raises its own, and it is the side that knows whether the text actually landed —
        // raising one here too meant the user heard the delivery announced twice, and left
        // a race for which of the two came last. When the model step was cancelled, losing
        // that race meant losing the cancel confirmation folded into the other one.
    }

    /// <summary>
    /// How long a recording may take to decode before the user is told it is happening. A
    /// short recording is ready well inside this, so the common case stays quiet; a long one
    /// would otherwise be silence with no explanation, which for a screen-reader user is
    /// indistinguishable from a dead button.
    /// </summary>
    private static readonly TimeSpan DecodeAnnouncementDelay = TimeSpan.FromMilliseconds(400);

    public async Task PlaySelectedSessionAsync()
    {
        // Captured once. The method now awaits, so the selection is free to move underneath
        // it, and every line below has to be talking about the same recording.
        SpeechSession? session = SelectedSession;
        if (session == null) return;

        // Asked against the session this class chose to play rather than against the player's
        // IsPlaying: decoding now happens off the UI thread, so there is a window where a file
        // is on its way to the speakers without being audible yet. Pressing the button in that
        // window has to stop it, not queue up a second copy of the same recording.
        if (_playingSession != null && PathsEqual(_playingSession.FilePath, session.FilePath))
        {
            StopPlayback();
            return;
        }

        try
        {
            StopPlayback();

            if (!File.Exists(session.FilePath))
            {
                StatusMessage?.Invoke(this, "Audio file not found.");
                RefreshSessions();
                return;
            }

            _playingSession = session;
            // Start each playback at the persisted speed so a saved preference is
            // honoured on launch and after navigating between sessions.
            _playbackPlayer.Speed = _settings.AudioSettings?.PlaybackSpeed ?? PlaybackSpeedOptions.Default;

            Task playback = _playbackPlayer.PlayAsync(session.FilePath);

            // Raised before the file is audible, because the button it drives is now the way
            // to stop a decode that is taking too long.
            PlaybackStarted?.Invoke(this, EventArgs.Empty);

            using (var announcement = new CancellationTokenSource())
            {
                Task waited = await Task.WhenAny(playback, Task.Delay(DecodeAnnouncementDelay, announcement.Token))
                    .ConfigureAwait(true);
                // Cancelled either way: a decode that beat the delay leaves a timer running
                // for every recording ever played otherwise.
                announcement.Cancel();

                // Only speak for a decode that is still the one the user is waiting for. Stop,
                // or a jump to another session, can land inside the delay — and announcing
                // then names a file that was cancelled, over the top of the one now playing.
                if (waited != playback && !playback.IsCompleted && IsStillPlaying(session))
                    StatusMessage?.Invoke(this, $"Decoding {session.FileName}...");
            }

            await playback.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StopPlayback();
            ErrorOccurred?.Invoke(this, $"Playback failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether the request that started this session is still the live one. False once Stop,
    /// a jump to another session, or the file reaching its end has cleared it.
    /// </summary>
    private bool IsStillPlaying(SpeechSession session) =>
        ReferenceEquals(_playingSession, session);

    public void StopPlayback()
    {
        try
        {
            _playbackPlayer.Stop();
        }
        catch { }

        _playingSession = null;
        
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    private void PlaybackPlayer_PlaybackEnded(object? sender, EventArgs args)
    {
        StopPlayback();
    }

    private void PlaybackPlayer_PlaybackFailed(object? sender, string errorMessage)
    {
        StopPlayback();
        ErrorOccurred?.Invoke(this, errorMessage);
    }

    public void Dispose()
    {
        _playbackPlayer.PlaybackEnded -= PlaybackPlayer_PlaybackEnded;
        _playbackPlayer.PlaybackFailed -= PlaybackPlayer_PlaybackFailed;
        _playbackPlayer.Dispose();
        // Cancels as it disposes, so a model call still in flight unwinds instead of
        // being abandoned mid-request.
        _llmOperation.Dispose();
    }

    public Task CleanupSessionsAsync()
    {
        var exclusions = new List<string>();
        if (SelectedSession != null)
            exclusions.Add(SelectedSession.FilePath);
        if (_playingSession != null)
            exclusions.Add(_playingSession.FilePath);
        if (_speechManager.CurrentRecordingSession != null)
            exclusions.Add(_speechManager.CurrentRecordingSession.FilePath);

        return _speechManager.CleanupSessionsAsync(exclusions);
    }

    public async Task EnsureStoppedAsync()
    {
        if (IsRecording)
        {
            await _speechManager.StopRecordingAsync();
        }
        if (IsTranscribing)
        {
            _speechManager.CancelTranscription();
        }
        // A model call can outlive the transcription that started it by minutes, so the
        // close path has to name it separately or the window waits on a request nobody is
        // left to read.
        _llmOperation.Cancel();
    }

    private static bool PathsEqual(string? p1, string? p2)
    {
        if (string.IsNullOrWhiteSpace(p1) || string.IsNullOrWhiteSpace(p2))
            return false;
        return string.Equals(Path.GetFullPath(p1), Path.GetFullPath(p2), StringComparison.OrdinalIgnoreCase);
    }
}
