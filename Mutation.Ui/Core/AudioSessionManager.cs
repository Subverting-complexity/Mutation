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
internal record TranscriptResult(string RawText, string? FormattedText = null);

internal class AudioSessionManager : IDisposable
{
    private readonly SpeechToTextManager _speechManager;
    private readonly AudioDeviceManager _audioDeviceManager;
    private readonly TranscriptFormatter _transcriptFormatter;
    private readonly Settings _settings;
    private readonly AudioPlayer _playbackPlayer;
    private SpeechSession? _playingSession;
    private SpeechSession? _selectedSession;
    private bool _currentRecordingUsesLlmFormatting;

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

    public event EventHandler? SelectedSessionChanged;
    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackStopped;
    public event EventHandler? StateChanged;
    
    public event EventHandler<TranscriptResult>? TranscriptReady;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string>? StatusMessage;

    public AudioSessionManager(
        SpeechToTextManager speechManager,
        AudioDeviceManager audioDeviceManager,
        TranscriptFormatter transcriptFormatter,
        Settings settings)
    {
        _speechManager = speechManager;
        _audioDeviceManager = audioDeviceManager;
        _transcriptFormatter = transcriptFormatter;
        _settings = settings;

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

    public async Task StartStopRecordingAsync(ISpeechToTextService activeService, bool useLlmFormatting, string prompt, string llmPrompt = "", CancellationToken cancellationToken = default)
    {
        try
        {
            if (IsTranscribing)
            {
                _speechManager.CancelTranscription();
                BeepPlayer.Play(BeepType.Failure);
                StateChanged?.Invoke(this, EventArgs.Empty);
                StatusMessage?.Invoke(this, "Transcription cancelled.");
                return;
            }

            if (!IsRecording)
            {
                _currentRecordingUsesLlmFormatting = useLlmFormatting;
                StopPlayback();
                
                StatusMessage?.Invoke(this, "Listening for audio...");
                StateChanged?.Invoke(this, EventArgs.Empty); // Notify UI to update buttons (Stop)

                var session = await _speechManager.StartRecordingAsync(_audioDeviceManager.MicrophoneDeviceIndex);
                RefreshSessions(session);
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _currentRecordingUsesLlmFormatting = useLlmFormatting;
                StopPlayback();
                StatusMessage?.Invoke(this, "Transcribing your recording...");
                StateChanged?.Invoke(this, EventArgs.Empty); // Notify UI to update buttons (Transcribing...)

                try
                {
                    string text = await _speechManager.StopRecordingAndTranscribeAsync(activeService, prompt, cancellationToken);
                    await ProcessTranscriptAsync(text, llmPrompt);
                }
                catch (OperationCanceledException)
                {
                    StatusMessage?.Invoke(this, "Transcription cancelled.");
                }
                finally
                {
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
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
            StateChanged?.Invoke(this, EventArgs.Empty);

            string text = await _speechManager.TranscribeExistingRecordingAsync(activeService, SelectedSession, prompt, cancellationToken);
            // Retry doesn't apply LLM formatting — pass raw text only so
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
            StateChanged?.Invoke(this, EventArgs.Empty);
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
            StateChanged?.Invoke(this, EventArgs.Empty);

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
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ProcessTranscriptAsync(string text, string llmPrompt)
    {
        // Always run rules-based formatting first
        string rulesFormattedText = _transcriptFormatter.ApplyRules(text, false);
        string? llmFormattedText = null;

        if (_currentRecordingUsesLlmFormatting)
        {
            try
            {
                StatusMessage?.Invoke(this, "Formatting with LLM...");
                string modelName = _settings.LlmSettings?.SelectedLlmModel ?? LlmSettings.DefaultModel;
                // Pass the rules-formatted text to the LLM
                llmFormattedText = await _transcriptFormatter.FormatWithLlmAsync(rulesFormattedText, llmPrompt, modelName);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"LLM formatting failed: {ex.Message}. Using rules-formatted transcript.");
                // llmFormattedText remains null — FinalizeTranscript will fall back to rules-only
            }
        }

        // Pass raw text and the final formatted text separately so that
        // FinalizeTranscript does not re-apply rules to LLM output.
        TranscriptReady?.Invoke(this, new TranscriptResult(text, llmFormattedText ?? rulesFormattedText));
        StatusMessage?.Invoke(this, "Transcript ready and copied.");
    }

    public Task PlaySelectedSessionAsync()
    {
        if (SelectedSession == null) return Task.CompletedTask;

        if (IsPlaying && _playingSession != null && PathsEqual(_playingSession.FilePath, SelectedSession.FilePath))
        {
            StopPlayback();
            return Task.CompletedTask;
        }

        try
        {
            StopPlayback();

            if (!File.Exists(SelectedSession.FilePath))
            {
                StatusMessage?.Invoke(this, "Audio file not found.");
                RefreshSessions();
                return Task.CompletedTask;
            }

            _playingSession = SelectedSession;
            _playbackPlayer.Play(SelectedSession.FilePath);
            
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StopPlayback();
            ErrorOccurred?.Invoke(this, $"Playback failed: {ex.Message}");
        }

        return Task.CompletedTask;
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
