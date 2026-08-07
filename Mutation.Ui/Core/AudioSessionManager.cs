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
public record TranscriptResult(
	string RawText,
	string? FormattedText = null,
	FastModeFallbackReason? FastModeNotice = null);

public class AudioSessionManager : IDisposable
{
    private readonly SpeechToTextManager _speechManager;
    private readonly AudioDeviceManager _audioDeviceManager;
    private readonly TranscriptFormatter _transcriptFormatter;
    private readonly Settings _settings;
    private readonly MicrophoneLevelWriteCoordinator _levelWriteCoordinator;
    private readonly FastModeNoticeTracker _fastModeNotices;
    private readonly AudioPlayer _playbackPlayer;
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

    public bool IsPlaying => _playbackPlayer.IsPlaying;
    public bool IsRecording => _speechManager.Recording;
    public bool IsTranscribing => _speechManager.Transcribing;

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
        if (IsRecording || IsTranscribing)
            return;

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
            if (IsTranscribing)
            {
                _speechManager.CancelTranscription();
                BeepPlayer.Play(BeepType.Failure);
                // No state change is raised here. Nothing has changed yet — only a
                // cancellation has been asked for — and the transcription that owns the
                // state raises its own Idle from its finally when it unwinds. Raising a
                // Transcribing here would race that Idle: both are delivered through the
                // dispatcher, and if the Idle landed first the window would settle on a
                // stale Transcribing with nothing left to correct it, leaving the buttons
                // disabled and the transcript box read-only for the rest of the session.
                // The beep and the status message are this branch's whole signal.
                StatusMessage?.Invoke(this, "Transcription cancelled.");
                return;
            }

            if (!IsRecording)
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
                    await ProcessTranscriptAsync(text, llmPrompt);
                }
                catch (OperationCanceledException)
                {
                    StatusMessage?.Invoke(this, "Transcription cancelled.");
                }
                catch (NoSpeechDetectedException)
                {
                    BeepPlayer.Play(BeepType.Failure);
                    StatusMessage?.Invoke(this, "No speech detected — nothing to transcribe.");
                }
                finally
                {
                    StateChanged?.Invoke(this, RecordingActivity.Idle);
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
        if (IsRecording || IsTranscribing)
        {
            StatusMessage?.Invoke(this, "Finish the current operation before retrying.");
            return;
        }

        if (SelectedSession == null)
        {
            StatusMessage?.Invoke(this, "No session available to retry.");
            return;
        }

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
            StatusMessage?.Invoke(this, "Transcription cancelled.");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            StateChanged?.Invoke(this, RecordingActivity.Idle);
        }
    }

    public async Task ImportAudioAsync(StorageFile file, ISpeechToTextService activeService, string prompt, CancellationToken cancellationToken = default)
    {
        if (IsRecording || IsTranscribing)
        {
            StatusMessage?.Invoke(this, "Finish the current operation before uploading.");
            return;
        }

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
            StatusMessage?.Invoke(this, "Transcription cancelled.");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            StateChanged?.Invoke(this, RecordingActivity.Idle);
        }
    }

    private async Task ProcessTranscriptAsync(string text, LlmSettings.LlmPrompt? llmPrompt)
    {
        // Always run rules-based formatting first
        string rulesFormattedText = _transcriptFormatter.ApplyRules(text, false);
        string? llmProcessedText = null;
        FastModeFallback? fastModeFallback = null;

        if (_currentRecordingUsesLlmProcessing && llmPrompt != null)
        {
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
                // Pass the rules-formatted text to the LLM
                llmProcessedText = await _transcriptFormatter.ProcessWithLlmAsync(rulesFormattedText, llmPrompt.Content, modelName, requestOptions);
            }
            catch (Exception ex)
            {
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
        // FinalizeTranscript does not re-apply rules to LLM output.
        TranscriptReady?.Invoke(this, new TranscriptResult(text, llmProcessedText ?? rulesFormattedText, fastModeNotice));
        StatusMessage?.Invoke(this, "Transcript ready and copied.");
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

            if (await Task.WhenAny(playback, Task.Delay(DecodeAnnouncementDelay)).ConfigureAwait(true) != playback)
                StatusMessage?.Invoke(this, $"Decoding {session.FileName}...");

            await playback.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StopPlayback();
            ErrorOccurred?.Invoke(this, $"Playback failed: {ex.Message}");
        }
    }

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
    }

    private static bool PathsEqual(string? p1, string? p2)
    {
        if (string.IsNullOrWhiteSpace(p1) || string.IsNullOrWhiteSpace(p2))
            return false;
        return string.Equals(Path.GetFullPath(p1), Path.GetFullPath(p2), StringComparison.OrdinalIgnoreCase);
    }
}
