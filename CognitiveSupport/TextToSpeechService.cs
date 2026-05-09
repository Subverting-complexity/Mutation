using System.Linq;
using System.Speech.Synthesis;

namespace CognitiveSupport
{
	public class TextToSpeechService : ITextToSpeechService
	{
		private readonly object _gate = new object();
		private SpeechSynthesizer? _synth;
		private string? _currentText;
		private bool _disposed;

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
			get { lock (_gate) return _currentText; }
		}

		public void Speak(string text, int rate, string? voiceName)
		{
			if (string.IsNullOrEmpty(text)) return;

			lock (_gate)
			{
				if (_disposed) return;

				EnsureSynth();
				_synth!.SpeakAsyncCancelAll();

				if (rate < -10) rate = -10;
				else if (rate > 10) rate = 10;
				_synth.Rate = rate;

				if (!string.IsNullOrWhiteSpace(voiceName))
				{
					try { _synth.SelectVoice(voiceName); }
					catch { /* fall back to default voice */ }
				}

				_currentText = text;
				_synth.SpeakAsync(text);
			}
		}

		public void Stop()
		{
			lock (_gate)
			{
				if (_synth is null) return;
				try { _synth.SpeakAsyncCancelAll(); }
				catch { }
				_currentText = null;
			}
		}

		private void EnsureSynth()
		{
			if (_synth is not null) return;
			_synth = new SpeechSynthesizer();
			_synth.SetOutputToDefaultAudioDevice();
			_synth.SpeakCompleted += OnSpeakCompleted;
		}

		private void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e)
		{
			lock (_gate)
			{
				if (_synth is not null && _synth.State == SynthesizerState.Ready)
					_currentText = null;
			}
		}

		public void Dispose()
		{
			lock (_gate)
			{
				if (_disposed) return;
				_disposed = true;
				if (_synth is not null)
				{
					try { _synth.SpeakAsyncCancelAll(); } catch { }
					_synth.SpeakCompleted -= OnSpeakCompleted;
					try { _synth.Dispose(); } catch { }
					_synth = null;
				}
				_currentText = null;
			}
		}
	}
}
