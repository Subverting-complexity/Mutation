using System.Linq;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;

namespace CognitiveSupport
{
	public class TextToSpeechService : ITextToSpeechService
	{
		private const string EndOfTextAnnouncement = "End of text.";
		private const string BeginningOfTextAnnouncement = "Beginning of text.";

		private const string PausedAnnouncement = "Paused.";

		private readonly object _gate = new object();
		private SpeechSynthesizer? _synth;
		// A second synthesizer reserved for short state cues (currently "Paused"). The main
		// synth is held in its native Paused state to preserve the exact word position, so it
		// cannot speak the cue without cancelling that held utterance; a separate synth speaks
		// the cue while the main read stays frozen and silent.
		private SpeechSynthesizer? _cueSynth;
		// Monotonic timestamp (Environment.TickCount64) captured when Pause() froze the read.
		// Resume() measures elapsed pause time against it to decide whether to rewind.
		private long _pausedAtTick;
		private string? _currentText;
		private string? _lastInputText;
		private int _currentPosition;
		// Maps the in-flight spoken string back to real-text offsets. A read with no
		// woven markers degrades to a single scalar shift, identical to the old
		// _spokenToCurrentDelta; a read with progress markers carries one breakpoint
		// per marker so OnSpeakProgress still reports the real position.
		private SpokenWeaveMap _spokenMap = SpokenWeaveMap.Empty;
		private List<int>? _sentenceStarts;
		// Authoritative navigation cursor: which sentence index we are on. Driven
		// explicitly by SkipSentence and nudged forward by playback progress, so
		// skip presses never race against the continuously-moving spoken position.
		private int _navIndex;
		// Monotonic timestamp (Environment.TickCount64) of when the cursor entered
		// the current sentence. Backward-skip uses this for the "am I at the start?"
		// grace window instead of the spoken character position.
		private long _sentenceEnteredAtTick;
		// The Prompt of the utterance currently owning progress/completion events.
		// Events from a cancelled prompt are ignored so stale callbacks can't corrupt
		// the cursor after a rapid skip.
		private Prompt? _currentPrompt;
		// Skip-burst tracking. Consecutive skip presses within the grace window form a
		// "burst" and step relative to the last skip target (_pendingSkipIndex) rather
		// than the playback position. This lets the user hold the modifiers and tap the
		// hotkey, stepping one sentence per tap, even when playback advances between taps
		// (which would otherwise move _navIndex out from under them and cause ping-pong).
		// Half of MinValue (not MinValue itself) so `now - _lastSkipTick` stays well
		// within long range — MinValue would overflow and wrongly report a burst.
		private const long NoRecentSkipTick = long.MinValue / 2;
		private long _lastSkipTick = NoRecentSkipTick;
		private int _pendingSkipIndex;
		private bool _speakingAnnouncement;
		private bool _disposed;
		// The token for whatever is currently being spoken. Always handed over inside
		// _gate so a superseded read cannot slip past the handover (issue #236).
		private readonly SupersedingOperation _operation = new();

		// Configurable announcement behaviour, pushed in from settings. Defaults mirror
		// the settings defaults so the service behaves sensibly before it is configured.
		private bool _announceReadingTimeAtStart = true;
		private int _announceReadingTimeMinimumMinutes = 1;
		private bool _announceProgressEnabled = true;
		private int _announceProgressEveryPercent = 25;
		private int _announceProgressMinimumMinutes = 2;

		// Highest progress threshold already announced (spoken) in the current read,
		// 0 = none. Advanced as each woven marker is reached so a re-plan after a
		// skip/resume never re-announces a threshold the listener already heard, and
		// backward navigation does not re-announce a passed threshold.
		private int _lastAnnouncedPercent;

		public event EventHandler<Exception>? SpeakFailed;

		public bool IsSpeaking
		{
			get
			{
				lock (_gate)
				{
					if (_synth is null) return false;
					return _synth.State == SynthesizerState.Speaking
						|| _synth.State == SynthesizerState.Paused;
				}
			}
		}

		public bool IsPaused
		{
			get
			{
				lock (_gate)
				{
					return _synth is not null && _synth.State == SynthesizerState.Paused;
				}
			}
		}

		public string? CurrentText
		{
			get { lock (_gate) return _lastInputText; }
		}

		public IReadOnlyList<string> GetVoiceNames()
		{
			lock (_gate)
			{
				if (_disposed) return Array.Empty<string>();
				EnsureSynth();
				return _synth!.GetInstalledVoices()
					.Where(v => v.Enabled)
					.Select(v => v.VoiceInfo.Name)
					.ToList();
			}
		}

		public void Speak(string text, int rate, int volume, string? voiceName, bool resumeIfSame, bool preprocess, int resumeRewindWordCount = 0, SpeechPreprocessingOptions? options = null)
		{
			if (string.IsNullOrEmpty(text)) return;

			CancellationToken token;
			lock (_gate)
			{
				if (_disposed) return;
				EnsureSynth();
				CancelAllAndClearPause();
				_speakingAnnouncement = false;
				token = _operation.Begin();
			}

			// The read itself runs off the caller's thread — preprocessing a long article
			// is not instant and this is called from the UI thread. Nothing awaits it, but
			// a failure is not lost either: the continuation reports it, so a read that
			// died (a voice that vanished, an audio endpoint that went away) is not
			// indistinguishable from one that finished.
			Task.Run(() => RunSpeak(text, rate, volume, voiceName, resumeIfSame, preprocess, resumeRewindWordCount, options, token))
				.ContinueWith(
					t => ReportSpeakFailure(t.Exception!.GetBaseException()),
					CancellationToken.None,
					TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
					TaskScheduler.Default);
		}

		// Hand a failed read to whoever is listening. Raised on a thread-pool thread, so
		// a UI subscriber has to marshal it. A listener that throws must not replace the
		// original failure with an unobserved one of its own.
		private void ReportSpeakFailure(Exception ex)
		{
			try { SpeakFailed?.Invoke(this, ex); }
			catch { }
		}

		private void RunSpeak(string text, int rate, int volume, string? voiceName, bool resumeIfSame, bool preprocess, int resumeRewindWordCount, SpeechPreprocessingOptions? options, CancellationToken token)
		{
			if (token.IsCancellationRequested) return;

			string processed = preprocess ? PreprocessForSpeech(text, options ?? SpeechPreprocessingOptions.All) : text;

			if (token.IsCancellationRequested || string.IsNullOrEmpty(processed)) return;

			lock (_gate)
			{
				if (token.IsCancellationRequested || _disposed) return;
				if (_synth is null) return;

				ApplyVoiceParams(rate, volume, voiceName);

				bool sameContent = string.Equals(processed, _currentText, StringComparison.Ordinal);
				bool canResume = resumeIfSame && sameContent
					&& _currentPosition > 0 && _currentPosition < processed.Length;

				if (canResume)
				{
					_lastInputText = text;
					// Rewind a few words before the stop point so the listener regains
					// context of where they are, then resume from there.
					int resumePos = RewindByWords(processed, _currentPosition, resumeRewindWordCount);
					_currentPosition = resumePos;
					EnterSentence(FindSentenceIndex(resumePos));
					ResetSkipBurst();
					// Mark the thresholds already passed at the resume point as announced so
					// resuming mid-read does not re-announce them; the weave plans only the
					// thresholds still ahead.
					_lastAnnouncedPercent = ProgressPercentAt(resumePos, processed.Length);
					_currentPrompt = _synth.SpeakAsync(BuildWovenSpeech(resumePos, string.Empty));
				}
				else
				{
					_currentText = processed;
					_lastInputText = text;
					_currentPosition = 0;
					_sentenceStarts = FindSentenceStarts(processed);
					EnterSentence(0);
					ResetSkipBurst();
					_lastAnnouncedPercent = 0;

					string warning = ShouldAnnounceLength(processed)
						? BuildLengthAnnouncement(processed)
						: string.Empty;
					_currentPrompt = _synth.SpeakAsync(BuildWovenSpeech(0, warning));
				}
			}
		}

		public void SkipSentence(int direction, int rate, int volume, string? voiceName, int graceWindowMs)
		{
			lock (_gate)
			{
				if (_disposed) return;
				if (_currentText is null || _sentenceStarts is null || _sentenceStarts.Count == 0)
					return;

				EnsureSynth();
				CancelAllAndClearPause();
				_speakingAnnouncement = false;
				_operation.Begin();

				ApplyVoiceParams(rate, volume, voiceName);

				int lastIndex = _sentenceStarts.Count - 1;
				long now = Environment.TickCount64;
				int desiredIndex = ComputeSkipTarget(
					direction, now, _lastSkipTick, _pendingSkipIndex,
					_navIndex, _sentenceEnteredAtTick, lastIndex, graceWindowMs);

				_lastSkipTick = now;

				if (desiredIndex < 0)
				{
					// Reached before the first sentence: announce the boundary, then keep
					// reading from the start rather than stopping. The boundary phrase is
					// woven in as the spoken-only prefix (like the length warning in
					// RunSpeak); progress markers ahead are re-planned from the start.
					_pendingSkipIndex = 0;
					EnterSentence(0);
					_currentPosition = 0;
					_lastAnnouncedPercent = 0;
					_currentPrompt = _synth!.SpeakAsync(
						BuildWovenSpeech(0, BeginningOfTextAnnouncement + " "));
				}
				else if (desiredIndex > lastIndex)
				{
					_pendingSkipIndex = lastIndex;
					_speakingAnnouncement = true;
					_currentPrompt = _synth!.SpeakAsync(EndOfTextAnnouncement);
				}
				else
				{
					_pendingSkipIndex = desiredIndex;
					int targetPos = _sentenceStarts[desiredIndex];
					EnterSentence(desiredIndex);
					_currentPosition = targetPos;
					// _lastAnnouncedPercent is deliberately preserved: a forward skip that
					// vaults past thresholds re-plans and announces only the highest at the
					// next boundary, and a backward skip never re-announces a passed one.
					_currentPrompt = _synth!.SpeakAsync(BuildWovenSpeech(targetPos, string.Empty));
				}
			}
		}

		// Move the navigation cursor onto a sentence and reset the grace-window timer.
		private void EnterSentence(int index)
		{
			_navIndex = index;
			_sentenceEnteredAtTick = Environment.TickCount64;
		}

		// Clear any in-progress skip burst so the next skip press is treated as fresh
		// (media-player semantics) and anchored to the current sentence.
		private void ResetSkipBurst()
		{
			_lastSkipTick = NoRecentSkipTick;
			_pendingSkipIndex = _navIndex;
		}

		// Pure decision for which sentence index a skip press targets. Returns a value
		// in [-1, lastIndex+1]; -1 means "before the first sentence" (beginning-of-text)
		// and lastIndex+1 means "past the last sentence" (end-of-text). Kept side-effect
		// free and static so the navigation rules can be unit-tested without a synthesizer.
		//
		// The handling is asymmetric because playback only ever drifts FORWARD:
		//
		//  - Forward always steps from the LIVE playback position (navIndex + 1). It can
		//    therefore never re-read the sentence currently playing and reliably reaches
		//    end-of-text — even if you navigated earlier and then listened on. (A frozen
		//    anchor would go stale here and re-read the last sentence instead of ending.)
		//  - Backward, in a "burst" (a press within graceWindowMs of the PREVIOUS press),
		//    steps from the last target (pendingSkipIndex), which playback never moves —
		//    so rapid back-taps keep marching even as playback drifts forward between taps
		//    (otherwise they'd ping-pong). Reaching before the first sentence -> -1.
		//  - The first backward press after a gap uses media-player semantics: at the
		//    first sentence there is nothing before it, so announce beginning-of-text;
		//    otherwise step to the previous sentence if we only just entered the current
		//    one, else restart the current sentence.
		internal static int ComputeSkipTarget(
			int direction, long now, long lastSkipTick, int pendingSkipIndex,
			int navIndex, long sentenceEnteredAtTick, int lastIndex, int graceWindowMs)
		{
			if (direction >= 0)
				return Math.Clamp(navIndex, 0, lastIndex) + 1;

			bool inBurst = now - lastSkipTick < graceWindowMs;
			if (inBurst)
				return Math.Clamp(pendingSkipIndex, 0, lastIndex) - 1;

			int currentIndex = Math.Clamp(navIndex, 0, lastIndex);
			if (currentIndex == 0)
				return -1; // already at the first sentence -> beginning of text

			bool justEnteredSentence = now - sentenceEnteredAtTick < graceWindowMs;
			return justEnteredSentence ? currentIndex - 1 : currentIndex;
		}

		public void SpeakAnnouncement(string text, int rate, int volume, string? voiceName)
		{
			if (string.IsNullOrEmpty(text)) return;

			lock (_gate)
			{
				if (_disposed) return;
				EnsureSynth();
				CancelAllAndClearPause();
				_operation.Begin();

				ApplyVoiceParams(rate, volume, voiceName);
				_speakingAnnouncement = true;
				_currentPrompt = _synth!.SpeakAsync(text);
			}
		}

		public ReadingPosition GetReadingPosition()
		{
			lock (_gate)
			{
				if (_currentText is null || _currentText.Length == 0
					|| _sentenceStarts is null || _sentenceStarts.Count == 0)
					return ReadingPosition.NotReading;

				int sentenceIndex = FindSentenceIndex(_currentPosition);
				return ReadingPosition.Create(
					_currentPosition, _currentText.Length, sentenceIndex, _sentenceStarts.Count);
			}
		}

		public void SetAnnouncementOptions(
			bool announceReadingTimeAtStart,
			int readingTimeMinimumMinutes,
			bool announceProgressEnabled,
			int announceProgressEveryPercent,
			int announceProgressMinimumMinutes)
		{
			lock (_gate)
			{
				_announceReadingTimeAtStart = announceReadingTimeAtStart;
				_announceReadingTimeMinimumMinutes = readingTimeMinimumMinutes;
				_announceProgressEnabled = announceProgressEnabled;
				_announceProgressEveryPercent = announceProgressEveryPercent > 0 ? announceProgressEveryPercent : 25;
				_announceProgressMinimumMinutes = announceProgressMinimumMinutes;
			}
		}

		public void Stop()
		{
			lock (_gate)
			{
				_operation.Cancel();
				if (_synth is null) return;
				try { _synth.SpeakAsyncCancelAll(); } catch { }
				// If the read was frozen by Pause(), the engine is still in its Paused state with
				// nothing queued. Release it so IsPaused returns false and the resume intent is
				// cleared — a Stop is a full halt, not a pause.
				if (_synth.State == SynthesizerState.Paused)
				{
					try { _synth.Resume(); } catch { }
				}
				_speakingAnnouncement = false;
				_currentPrompt = null;
			}
		}

		public void Pause(int rate, int volume, string? voiceName)
		{
			lock (_gate)
			{
				if (_disposed || _synth is null) return;
				// Only freeze an actively-speaking read. Already paused, stopped, or speaking a
				// transient announcement: nothing to pause.
				if (_synth.State != SynthesizerState.Speaking || _speakingAnnouncement) return;

				_synth.Pause();
				_pausedAtTick = Environment.TickCount64;
			}

			// Speak the cue outside the engine state above so the held main utterance is never
			// touched; the cue runs on a separate synth and the main read stays frozen.
			SpeakCue(PausedAnnouncement, rate, volume, voiceName);
		}

		public void Resume(int rate, int volume, string? voiceName, int resumeRewindWordCount, int rewindAfterPauseSeconds)
		{
			lock (_gate)
			{
				if (_disposed || _synth is null) return;
				if (_synth.State != SynthesizerState.Paused) return;

				long elapsedMs = Environment.TickCount64 - _pausedAtTick;
				bool rewind = ShouldRewindAfterPause(elapsedMs, rewindAfterPauseSeconds);

				// Within the threshold (or nothing to rewind into): continue from the exact word
				// using the engine's native resume. No cancel, no respeak — seamless.
				if (!rewind || _currentText is null)
				{
					_synth.Resume();
					return;
				}

				// Beyond the threshold: cancel the frozen utterance and respeak from a rewound
				// position so the listener regains context. Mirrors RunSpeak's resume branch.
				ApplyVoiceParams(rate, volume, voiceName);

				int resumePos = ComputeResumePosition(_currentText, _currentPosition, resumeRewindWordCount, rewind: true);
				_currentPosition = resumePos;
				EnterSentence(FindSentenceIndex(resumePos));
				ResetSkipBurst();
				// Mark thresholds passed at the resume point as announced; weave only ahead.
				_lastAnnouncedPercent = ProgressPercentAt(resumePos, _currentText.Length);

				try { _synth.SpeakAsyncCancelAll(); } catch { }
				_currentPrompt = _synth.SpeakAsync(BuildWovenSpeech(resumePos, string.Empty));
				// The engine is still in its paused state; un-pause it so the newly queued
				// utterance actually plays.
				try { _synth.Resume(); } catch { }
			}
		}

		// Pure decision for whether a resume should rewind for context. A pause at or below the
		// threshold resumes seamlessly (false); one that exceeds it rewinds (true). A threshold
		// of 0 (or negative, defensively) always rewinds. Side-effect free so the threshold
		// branch is unit-testable with explicit elapsed values, no real waits.
		internal static bool ShouldRewindAfterPause(long elapsedMs, int rewindAfterPauseSeconds)
		{
			if (rewindAfterPauseSeconds <= 0) return true;
			return elapsedMs > (long)rewindAfterPauseSeconds * 1000;
		}

		// Pure resume-position computation: rewind by words for context, or hold the exact
		// position when resuming seamlessly. Kept static so both branches can be unit-tested.
		internal static int ComputeResumePosition(string text, int currentPosition, int rewindWordCount, bool rewind)
			=> rewind ? RewindByWords(text, currentPosition, rewindWordCount) : currentPosition;

		// Speak a short state cue on the dedicated cue synthesizer, leaving the main synth (and
		// any utterance it holds paused) untouched.
		private void SpeakCue(string text, int rate, int volume, string? voiceName)
		{
			if (string.IsNullOrEmpty(text)) return;
			lock (_gate)
			{
				if (_disposed) return;
				if (_cueSynth is null)
				{
					_cueSynth = new SpeechSynthesizer();
					_cueSynth.SetOutputToDefaultAudioDevice();
				}
				ApplyVoiceParamsTo(_cueSynth, rate, volume, voiceName);
				try { _cueSynth.SpeakAsyncCancelAll(); } catch { }
				_cueSynth.SpeakAsync(text);
			}
		}

		// Cancel any in-flight utterance and release a native pause, so a freshly queued
		// utterance is never stuck behind a frozen engine. Callers hold _gate and have ensured
		// _synth is non-null (via EnsureSynth).
		private void CancelAllAndClearPause()
		{
			try { _synth!.SpeakAsyncCancelAll(); } catch { }
			if (_synth!.State == SynthesizerState.Paused)
			{
				try { _synth.Resume(); } catch { }
			}
		}

		private void ApplyVoiceParams(int rate, int volume, string? voiceName)
			=> ApplyVoiceParamsTo(_synth!, rate, volume, voiceName);

		private static void ApplyVoiceParamsTo(SpeechSynthesizer synth, int rate, int volume, string? voiceName)
		{
			if (rate < -10) rate = -10;
			else if (rate > 10) rate = 10;
			synth.Rate = rate;

			if (volume < 0) volume = 0;
			else if (volume > 100) volume = 100;
			synth.Volume = volume;

			if (!string.IsNullOrWhiteSpace(voiceName))
			{
				try { synth.SelectVoice(voiceName); }
				catch { /* fall back to default voice */ }
			}
		}

		private void EnsureSynth()
		{
			if (_synth is not null) return;
			_synth = new SpeechSynthesizer();
			_synth.SetOutputToDefaultAudioDevice();
			_synth.SpeakProgress += OnSpeakProgress;
			_synth.SpeakCompleted += OnSpeakCompleted;
		}

		private void OnSpeakProgress(object? sender, SpeakProgressEventArgs e)
		{
			lock (_gate)
			{
				// Ignore stragglers from a cancelled/superseded utterance.
				if (!ReferenceEquals(e.Prompt, _currentPrompt)) return;
				if (_speakingAnnouncement) return;
				if (_currentText is null) return;
				int mapped = _spokenMap.ToReal(e.CharacterPosition);
				_currentPosition = mapped;

				// Advance the announced tracker as each baked progress marker is reached, so
				// a later skip/resume re-plan never re-announces a threshold already heard.
				int reached = _spokenMap.HighestMarkerPercentAtOrBefore(mapped);
				if (reached > _lastAnnouncedPercent)
					_lastAnnouncedPercent = reached;

				// Keep the navigation cursor in step with natural playback so a skip
				// after the reader has crossed into a new sentence acts from there.
				// Re-entering a sentence also restarts the back-skip grace window.
				int sentenceIndex = FindSentenceIndex(mapped);
				if (sentenceIndex != _navIndex)
					EnterSentence(sentenceIndex);
			}
		}

		// Build the single spoken string for a read that starts at real offset `fromReal`,
		// weaving every still-upcoming progress announcement into it at sentence boundaries
		// so the engine reads straight through with no cancel/restart seam. `prefix` is
		// spoken-only lead-in text (a length or beginning-of-text announcement); pass "" for
		// none. Records the spoken->real map as a side effect and returns the string to
		// speak. Caller holds _gate with _currentText and _sentenceStarts set.
		private string BuildWovenSpeech(int fromReal, string prefix)
		{
			string real = _currentText!;
			int length = real.Length;

			int step = _announceProgressEnabled
				&& ReadingEstimate.ExceedsMinutes(length, _announceProgressMinimumMinutes)
					? _announceProgressEveryPercent
					: 0;

			var weaves = ProgressWeavePlanner.Plan(
				length, _sentenceStarts!, fromReal, _lastAnnouncedPercent, step);

			var markers = new List<SpokenWeave.Marker>(weaves.Count);
			foreach (var w in weaves)
			{
				// Rate is constant during a read, so the minutes-remaining at each boundary
				// can be computed now, at weave time.
				int minutes = ReadingEstimate.RemainingWholeMinutes(length - w.RealOffset);
				string text = ReadingAnnouncements.Progress(w.Percent, minutes) + " ";
				markers.Add(new SpokenWeave.Marker(w.RealOffset, w.Percent, text));
			}

			var (spoken, map) = SpokenWeave.Build(real, fromReal, prefix, markers);
			_spokenMap = map;
			return spoken;
		}

		private static int ProgressPercentAt(int position, int length)
		{
			if (length <= 0) return 0;
			int pos = Math.Clamp(position, 0, length);
			return (int)((double)pos * 100d / length);
		}

		private void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e)
		{
			lock (_gate)
			{
				if (!ReferenceEquals(e.Prompt, _currentPrompt)) return;
				if (_speakingAnnouncement)
				{
					_speakingAnnouncement = false;
					return;
				}
				if (!e.Cancelled && _currentText is not null)
				{
					_currentPosition = _currentText.Length;
					_navIndex = _sentenceStarts is { Count: > 0 } ? _sentenceStarts.Count - 1 : 0;
					// A threshold whose boundary fell inside the final sentence was never
					// woven (it has no following boundary), so nothing is announced here.
					// The read has reached 100%, which is never announced as progress —
					// "End of text." speaks for itself.
				}
			}
		}

		// Move `position` backward past `wordCount` whitespace-delimited words so a
		// resumed read re-speaks a little prior context. Clamped to the start of the
		// text. Kept side-effect free and static so it can be unit-tested. If `position`
		// falls mid-word, that partial word counts as the first word stepped over.
		internal static int RewindByWords(string text, int position, int wordCount)
		{
			if (wordCount <= 0) return position;
			int i = Math.Clamp(position, 0, text.Length);
			for (int w = 0; w < wordCount && i > 0; w++)
			{
				while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
				while (i > 0 && !char.IsWhiteSpace(text[i - 1])) i--;
			}
			return i;
		}

		private int FindSentenceIndex(int position)
		{
			if (_sentenceStarts is null || _sentenceStarts.Count == 0) return 0;
			int idx = _sentenceStarts.BinarySearch(position);
			if (idx >= 0) return idx;
			return Math.Max(0, ~idx - 1);
		}

		public void Dispose()
		{
			lock (_gate)
			{
				if (_disposed) return;
				_disposed = true;
				_operation.Dispose();
				if (_synth is not null)
				{
					try { _synth.SpeakAsyncCancelAll(); } catch { }
					_synth.SpeakProgress -= OnSpeakProgress;
					_synth.SpeakCompleted -= OnSpeakCompleted;
					try { _synth.Dispose(); } catch { }
					_synth = null;
				}
				if (_cueSynth is not null)
				{
					try { _cueSynth.SpeakAsyncCancelAll(); } catch { }
					try { _cueSynth.Dispose(); } catch { }
					_cueSynth = null;
				}
				_currentText = null;
				_lastInputText = null;
				_sentenceStarts = null;
				_currentPrompt = null;
			}
		}

		private static readonly Regex SentenceEndRegex = new(
			@"[.!?]+(?:[""')\]]+)?\s+",
			RegexOptions.Compiled);

		private static List<int> FindSentenceStarts(string text)
		{
			var starts = new List<int> { 0 };
			foreach (Match m in SentenceEndRegex.Matches(text))
			{
				int start = m.Index + m.Length;
				if (start < text.Length && start != starts[^1])
					starts.Add(start);
			}
			return starts;
		}

		// The startup reading-time announcement plays when it is enabled AND the estimated
		// read is longer than the configured minimum minutes. This replaces the old fixed
		// 5000-character cutoff with a minutes-based threshold from the shared estimate.
		private bool ShouldAnnounceLength(string text)
			=> _announceReadingTimeAtStart
				&& ReadingEstimate.ExceedsMinutes(text.Length, _announceReadingTimeMinimumMinutes);

		private static string BuildLengthAnnouncement(string text)
			=> ReadingAnnouncements.StartupReadingTime(ReadingEstimate.SpokenWholeMinutes(text.Length));

		private static readonly Regex CodeFenceRegex = new(@"```[\s\S]*?```", RegexOptions.Compiled);
		private static readonly Regex InlineCodeRegex = new(@"`([^`\n]+)`", RegexOptions.Compiled);
		private static readonly Regex HeadingRegex = new(@"(?m)^[ \t]*#{1,6}[ \t]+", RegexOptions.Compiled);
		private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled);
		private static readonly char[] UrlTrailingPunctuation = { '.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'' };

		private static string ReplaceUrlWithHost(Match match)
		{
			string raw = match.Value;
			int end = raw.Length;
			while (end > 0 && Array.IndexOf(UrlTrailingPunctuation, raw[end - 1]) >= 0)
				end--;

			string url = raw.Substring(0, end);
			string trailing = raw.Substring(end);

			if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
				return $"link to {uri.Host}{trailing}";

			return raw;
		}
		private static readonly Regex BoldRegex = new(@"\*\*([^*\n]+)\*\*", RegexOptions.Compiled);
		private static readonly Regex ItalicStarRegex = new(@"(?<!\*)\*([^*\n]+)\*(?!\*)", RegexOptions.Compiled);
		private static readonly Regex ItalicUnderscoreRegex = new(@"(?<![A-Za-z0-9_])_([^_\n]+)_(?![A-Za-z0-9_])", RegexOptions.Compiled);
		private static readonly Regex BulletRegex = new(@"(?m)^[ \t]*[-*+][ \t]+", RegexOptions.Compiled);
		private static readonly Regex ParagraphBreakRegex = new(@"\r?\n[ \t]*\r?\n+", RegexOptions.Compiled);
		private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
		private static readonly Regex AbbrevEgRegex = new(@"\be\.g\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex AbbrevIeRegex = new(@"\bi\.e\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex AbbrevEtcRegex = new(@"\betc\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex AbbrevVsRegex = new(@"\bvs\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		// Apply every cleanup rule. Kept for callers that always want the full
		// pass; delegates to the per-rule overload with all rules enabled.
		public static string PreprocessForSpeech(string text)
			=> PreprocessForSpeech(text, SpeechPreprocessingOptions.All);

		// Apply each cleanup rule only when its toggle in <paramref name="options"/>
		// is on. Steps run in the same order as the original all-on pass, so the
		// result with every rule enabled is identical to the previous behaviour.
		public static string PreprocessForSpeech(string text, SpeechPreprocessingOptions options)
		{
			if (string.IsNullOrEmpty(text)) return text;
			options ??= SpeechPreprocessingOptions.All;

			string s = text;
			if (options.RemoveCodeBlocks)
				s = CodeFenceRegex.Replace(s, " ");
			if (options.StripBoldItalicCode)
				s = InlineCodeRegex.Replace(s, "$1");
			if (options.StripHeadingMarks)
				s = HeadingRegex.Replace(s, string.Empty);
			if (options.ShortenWebLinks)
				s = UrlRegex.Replace(s, ReplaceUrlWithHost);
			if (options.StripBoldItalicCode)
			{
				s = BoldRegex.Replace(s, "$1");
				s = ItalicStarRegex.Replace(s, "$1");
				s = ItalicUnderscoreRegex.Replace(s, "$1");
			}
			if (options.StripBulletMarkers)
				s = BulletRegex.Replace(s, string.Empty);
			if (options.ExpandAbbreviations)
			{
				s = AbbrevEgRegex.Replace(s, "for example");
				s = AbbrevIeRegex.Replace(s, "that is");
				s = AbbrevEtcRegex.Replace(s, "et cetera");
				s = AbbrevVsRegex.Replace(s, "versus");
			}
			if (options.NormaliseWhitespace)
			{
				s = ParagraphBreakRegex.Replace(s, ". ");
				s = WhitespaceRegex.Replace(s, " ");
			}
			return s.Trim();
		}
	}
}
