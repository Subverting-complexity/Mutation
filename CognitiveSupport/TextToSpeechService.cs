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

		public void Speak(string text, int rate, int volume, string? voiceName, bool resumeIfSame, bool preprocess)
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
			Task.Run(() => RunSpeak(text, rate, volume, voiceName, resumeIfSame, preprocess, token));
		}

		private void RunSpeak(string text, int rate, int volume, string? voiceName, bool resumeIfSame, bool preprocess, CancellationToken token)
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
					_spokenToCurrentDelta = _currentPosition;
					EnterSentence(FindSentenceIndex(_currentPosition));
					_currentPrompt = _synth.SpeakAsync(processed.Substring(_currentPosition));
				}
				else
				{
					_currentText = processed;
					_lastInputText = text;
					_currentPosition = 0;
					_sentenceStarts = FindSentenceStarts(processed);
					EnterSentence(0);

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
				int currentIndex = Math.Clamp(_navIndex, 0, lastIndex);

				int targetIndex = currentIndex;
				string? boundaryAnnouncement = null;
				if (direction < 0)
				{
					// Media-player semantics: if we have only just entered the current
					// sentence (within the grace window) a back-press steps to the
					// previous sentence; otherwise it restarts the current one. Because
					// the grace timer resets on every entry, a rapid burst of presses
					// keeps stepping back one sentence at a time.
					long elapsedMs = Environment.TickCount64 - _sentenceEnteredAtTick;
					bool atSentenceStart = elapsedMs < graceWindowMs;
					if (atSentenceStart && currentIndex == 0)
						boundaryAnnouncement = BeginningOfTextAnnouncement; // nothing before the first sentence
					else
						targetIndex = atSentenceStart ? currentIndex - 1 : currentIndex;
				}
				else
				{
					if (currentIndex >= lastIndex)
						boundaryAnnouncement = EndOfTextAnnouncement; // nothing after the final sentence
					else
						targetIndex = currentIndex + 1;
				}

				if (boundaryAnnouncement is not null)
				{
					_speakingAnnouncement = true;
					_currentPrompt = _synth!.SpeakAsync(boundaryAnnouncement);
				}
				else
				{
					int targetPos = _sentenceStarts[targetIndex];
					EnterSentence(targetIndex);
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
