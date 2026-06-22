using System.Linq;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;

namespace CognitiveSupport
{
	public class TextToSpeechService : ITextToSpeechService
	{
		private const int LengthWarningThresholdChars = 5000;
		private const string EndOfTextAnnouncement = "End of text.";
		private const string BeginningOfTextAnnouncement = "Beginning of text.";

		private readonly object _gate = new object();
		private SpeechSynthesizer? _synth;
		private string? _currentText;
		private string? _lastInputText;
		private int _currentPosition;
		private int _spokenToCurrentDelta;
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
		private CancellationTokenSource? _opCts;

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

		public void Speak(string text, int rate, int volume, string? voiceName, bool resumeIfSame, bool preprocess, int resumeRewindWordCount = 0)
		{
			if (string.IsNullOrEmpty(text)) return;

			CancellationTokenSource? oldCts;
			CancellationTokenSource newCts = new();
			lock (_gate)
			{
				if (_disposed) { newCts.Dispose(); return; }
				EnsureSynth();
				try { _synth!.SpeakAsyncCancelAll(); } catch { }
				_speakingAnnouncement = false;
				oldCts = _opCts;
				_opCts = newCts;
			}
			oldCts?.Cancel();
			oldCts?.Dispose();

			CancellationToken token = newCts.Token;
			Task.Run(() => RunSpeak(text, rate, volume, voiceName, resumeIfSame, preprocess, resumeRewindWordCount, token));
		}

		private void RunSpeak(string text, int rate, int volume, string? voiceName, bool resumeIfSame, bool preprocess, int resumeRewindWordCount, CancellationToken token)
		{
			if (token.IsCancellationRequested) return;

			string processed;
			try { processed = preprocess ? PreprocessForSpeech(text) : text; }
			catch { return; }

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
					_spokenToCurrentDelta = resumePos;
					EnterSentence(FindSentenceIndex(resumePos));
					ResetSkipBurst();
					_currentPrompt = _synth.SpeakAsync(processed.Substring(resumePos));
				}
				else
				{
					_currentText = processed;
					_lastInputText = text;
					_currentPosition = 0;
					_sentenceStarts = FindSentenceStarts(processed);
					EnterSentence(0);
					ResetSkipBurst();

					string warning = ShouldAnnounceLength(processed)
						? BuildLengthAnnouncement(processed)
						: string.Empty;
					string spoken = warning + processed;
					_spokenToCurrentDelta = -warning.Length;
					_currentPrompt = _synth.SpeakAsync(spoken);
				}
			}
		}

		public void SkipSentence(int direction, int rate, int volume, string? voiceName, int graceWindowMs)
		{
			CancellationTokenSource? oldCts;
			CancellationTokenSource newCts = new();
			lock (_gate)
			{
				if (_disposed) { newCts.Dispose(); return; }
				if (_currentText is null || _sentenceStarts is null || _sentenceStarts.Count == 0)
				{
					newCts.Dispose();
					return;
				}

				EnsureSynth();
				try { _synth!.SpeakAsyncCancelAll(); } catch { }
				_speakingAnnouncement = false;
				oldCts = _opCts;
				_opCts = newCts;

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
					// reading from the start rather than stopping. The announcement is
					// prepended to the text (like the length warning in RunSpeak) and the
					// spoken->current delta offsets it so progress maps back onto sentence 0.
					_pendingSkipIndex = 0;
					EnterSentence(0);
					_currentPosition = 0;
					string announcement = BeginningOfTextAnnouncement + " ";
					_spokenToCurrentDelta = -announcement.Length;
					_currentPrompt = _synth!.SpeakAsync(announcement + _currentText);
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
					_spokenToCurrentDelta = targetPos;
					_currentPrompt = _synth!.SpeakAsync(_currentText.Substring(targetPos));
				}
			}
			oldCts?.Cancel();
			oldCts?.Dispose();
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

			CancellationTokenSource? oldCts;
			CancellationTokenSource newCts = new();
			lock (_gate)
			{
				if (_disposed) { newCts.Dispose(); return; }
				EnsureSynth();
				try { _synth!.SpeakAsyncCancelAll(); } catch { }
				oldCts = _opCts;
				_opCts = newCts;

				ApplyVoiceParams(rate, volume, voiceName);
				_speakingAnnouncement = true;
				_currentPrompt = _synth!.SpeakAsync(text);
			}
			oldCts?.Cancel();
			oldCts?.Dispose();
		}

		public void Stop()
		{
			CancellationTokenSource? oldCts;
			lock (_gate)
			{
				oldCts = _opCts;
				_opCts = null;
				if (_synth is null) return;
				try { _synth.SpeakAsyncCancelAll(); } catch { }
				_speakingAnnouncement = false;
				_currentPrompt = null;
			}
			oldCts?.Cancel();
			oldCts?.Dispose();
		}

		private void ApplyVoiceParams(int rate, int volume, string? voiceName)
		{
			if (rate < -10) rate = -10;
			else if (rate > 10) rate = 10;
			_synth!.Rate = rate;

			if (volume < 0) volume = 0;
			else if (volume > 100) volume = 100;
			_synth.Volume = volume;

			if (!string.IsNullOrWhiteSpace(voiceName))
			{
				try { _synth.SelectVoice(voiceName); }
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
				int mapped = e.CharacterPosition + _spokenToCurrentDelta;
				if (mapped < 0) mapped = 0;
				if (mapped > _currentText.Length) mapped = _currentText.Length;
				_currentPosition = mapped;

				// Keep the navigation cursor in step with natural playback so a skip
				// after the reader has crossed into a new sentence acts from there.
				// Re-entering a sentence also restarts the back-skip grace window.
				int sentenceIndex = FindSentenceIndex(mapped);
				if (sentenceIndex != _navIndex)
					EnterSentence(sentenceIndex);
			}
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
			CancellationTokenSource? oldCts;
			lock (_gate)
			{
				if (_disposed) return;
				_disposed = true;
				oldCts = _opCts;
				_opCts = null;
				if (_synth is not null)
				{
					try { _synth.SpeakAsyncCancelAll(); } catch { }
					_synth.SpeakProgress -= OnSpeakProgress;
					_synth.SpeakCompleted -= OnSpeakCompleted;
					try { _synth.Dispose(); } catch { }
					_synth = null;
				}
				_currentText = null;
				_lastInputText = null;
				_sentenceStarts = null;
				_currentPrompt = null;
			}
			oldCts?.Cancel();
			oldCts?.Dispose();
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

		private static bool ShouldAnnounceLength(string text) => text.Length > LengthWarningThresholdChars;

		private static string BuildLengthAnnouncement(string text)
		{
			int wordsApprox = Math.Max(1, text.Length / 5);
			int wpm = 180;
			int minutes = (int)Math.Round((double)wordsApprox / wpm);
			if (minutes < 1) minutes = 1;
			string unit = minutes == 1 ? "minute" : "minutes";
			return $"Reading approximately {minutes} {unit} of text. ";
		}

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

		public static string PreprocessForSpeech(string text)
		{
			if (string.IsNullOrEmpty(text)) return text;
			string s = text;
			s = CodeFenceRegex.Replace(s, " ");
			s = InlineCodeRegex.Replace(s, "$1");
			s = HeadingRegex.Replace(s, string.Empty);
			s = UrlRegex.Replace(s, ReplaceUrlWithHost);
			s = BoldRegex.Replace(s, "$1");
			s = ItalicStarRegex.Replace(s, "$1");
			s = ItalicUnderscoreRegex.Replace(s, "$1");
			s = BulletRegex.Replace(s, string.Empty);
			s = AbbrevEgRegex.Replace(s, "for example");
			s = AbbrevIeRegex.Replace(s, "that is");
			s = AbbrevEtcRegex.Replace(s, "et cetera");
			s = AbbrevVsRegex.Replace(s, "versus");
			s = ParagraphBreakRegex.Replace(s, ". ");
			s = WhitespaceRegex.Replace(s, " ");
			return s.Trim();
		}
	}
}
