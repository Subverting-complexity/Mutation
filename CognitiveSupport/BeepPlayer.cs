using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CognitiveSupport;

public enum BeepType { Start, Success, Failure, End, Mute, Unmute, Waiting }

public static class BeepPlayer
{
	public const int DefaultStartFrequency = 970;
	public const int DefaultStartDuration = 80;
	public static readonly (int Frequency, int Duration)[] DefaultSuccessSequence = new[] { (1050, 40), (1150, 40) };
	public const int DefaultFailureFrequency = 300;
	public const int DefaultFailureDuration = 100;
	public const int DefaultFailureRepeats = 3;
	public const int DefaultEndFrequency = 800;
	public const int DefaultEndDuration = 50;
	public const int DefaultMuteFrequency = 500;
	public const int DefaultMuteDuration = 200;
	public const int DefaultUnmuteFrequency = 1300;
	public const int DefaultUnmuteDuration = 50;
	// Two low, slow tones — deliberately nothing like the single bright Start beep, so a
	// hotkey press that is waiting on the microphone cannot be mistaken for one that has
	// opened it. Silence was the alternative, and to someone driving the app from another
	// window by ear, silence reads as a shortcut that did not register (issue #312).
	// Falling, where Success rises, so the two cannot be confused either.
	public static readonly (int Frequency, int Duration)[] DefaultWaitingSequence = new[] { (440, 90), (350, 90) };

	// Upper bound on how many times a beep is repeated in one PlayRepeated call. Keeps a
	// runaway retry count from synthesizing a multi-second WAV the user has to sit through.
	public const int MaxRepeatCount = 10;

	private static readonly object SyncLock = new();
	private static readonly TimeSpan DuplicateSuppressWindow = TimeSpan.FromMilliseconds(500);
	private static readonly Dictionary<BeepType, DateTime> _lastPlayed = new();

	// The user's own sound files, decoded once when settings are loaded. Empty when custom
	// beeps are switched off, or when a file could not be read — either way the synthesized
	// tone below is played instead, so the app is never silent about an outcome.
	private static readonly Dictionary<BeepType, BeepClip> _customClips = new();

	// The synthesized tones, built on first use and kept. Keyed by repeat count as well as
	// type because a repeat of a default beep is one longer sequence with gaps in it, not the
	// same sound played twice — that is what lets a listener count the retries (issue #216).
	private static readonly Dictionary<(BeepType Type, int RepeatCount), BeepClip> _defaultClips = new();

	private static BeepAudioOutput? _output;

	public static IReadOnlyList<string> LastInitializationIssues { get; private set; } = Array.Empty<string>();

	public static void Initialize(Settings settings)
	{
		lock (SyncLock)
		{
			_customClips.Clear();
			var issues = new List<string>();
			var custom = settings.AudioSettings?.CustomBeepSettings;
			if (custom?.UseCustomBeeps == true)
			{
				Load(BeepType.Start, custom.ResolveAudioFilePath(custom.BeepStartFile ?? string.Empty), "start", issues);
				Load(BeepType.Success, custom.ResolveAudioFilePath(custom.BeepSuccessFile ?? string.Empty), "success", issues);
				Load(BeepType.Failure, custom.ResolveAudioFilePath(custom.BeepFailureFile ?? string.Empty), "failure", issues);
				Load(BeepType.End, custom.ResolveAudioFilePath(custom.BeepEndFile ?? string.Empty), "end", issues);
				Load(BeepType.Mute, custom.ResolveAudioFilePath(custom.BeepMuteFile ?? string.Empty), "mute", issues);
				Load(BeepType.Unmute, custom.ResolveAudioFilePath(custom.BeepUnmuteFile ?? string.Empty), "unmute", issues);
			}
			LastInitializationIssues = issues;

			// Opening an audio device is the one slow step left in the beep path, so it is done
			// here — at startup, and again after a settings save — rather than under a user who
			// is waiting to hear whether their dictation landed (issue #386).
			Output().Warm();
		}
	}

	private static void Load(BeepType type, string filePath, string name, List<string> issues)
	{
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
			return;

		try
		{
			_customClips[type] = BeepClipReader.ReadFile(filePath, BeepAudioOutput.SampleRate, BeepAudioOutput.Channels);
		}
		catch
		{
			issues.Add($"Could not load {name} beep file: {filePath}");
		}
	}

	public static void Play(BeepType type)
	{
		lock (SyncLock)
		{
			var now = DateTime.UtcNow;
			if (_lastPlayed.TryGetValue(type, out var last) && (now - last) < DuplicateSuppressWindow)
			{
				// Suppress near-duplicate beep
				return;
			}
			_lastPlayed[type] = now;

			PlayCore(type, repeatCount: 1);
		}
	}

	/// <summary>
	/// Plays <paramref name="repeatCount"/> copies of a beep so the listener can count them.
	/// <para>
	/// Calling <see cref="Play"/> in a loop does not work: the duplicate-suppression window
	/// above swallows every call after the first (issue #216). The default beeps are therefore
	/// synthesized as one sequence with gaps in it, and a custom sound file is played
	/// back-to-back the requested number of times.
	/// </para>
	/// </summary>
	public static void PlayRepeated(BeepType type, int repeatCount)
	{
		repeatCount = ClampRepeatCount(repeatCount);

		lock (SyncLock)
		{
			// Deliberately not consulting the suppression window: this call *is* the
			// repeat, so it must never be collapsed. Still stamped so a stray single
			// Play right behind it stays suppressed.
			_lastPlayed[type] = DateTime.UtcNow;

			PlayCore(type, repeatCount);
		}
	}

	// Held under SyncLock by both callers. Everything here is a dictionary lookup or a handover
	// to the output's own thread, so the lock is never held across anything that waits.
	private static void PlayCore(BeepType type, int repeatCount)
	{
		try
		{
			if (_customClips.TryGetValue(type, out var custom))
			{
				Output().Play(custom, repeatCount);
				return;
			}

			Output().Play(DefaultClip(type, repeatCount));
		}
		catch
		{
			// A beep that will not play must never take down the operation it is reporting on:
			// this runs inside Polly retry lambdas, where an escaping exception aborts the
			// transcription. Playback itself cannot throw here — it is a handover to another
			// thread — but synthesizing a default tone for the first time can.
		}
	}

	private static BeepClip DefaultClip(BeepType type, int repeatCount)
	{
		var key = (type, ClampRepeatCount(repeatCount));
		if (_defaultClips.TryGetValue(key, out var cached))
			return cached;

		var wav = BeepToneSynthesizer.SynthesizeWav(GetRepeatedSequence(key.Item1, key.Item2));
		var clip = BeepClipReader.ReadBytes(wav, BeepAudioOutput.SampleRate, BeepAudioOutput.Channels);
		_defaultClips[key] = clip;
		return clip;
	}

	private static BeepAudioOutput Output() => _output ??= new BeepAudioOutput();

	private static int ClampRepeatCount(int repeatCount) => Math.Clamp(repeatCount, 1, MaxRepeatCount);

	// The tone sequence for a beep played <paramref name="repeatCount"/> times in a row.
	// Exposed so the repeat behaviour is verifiable without an audio device.
	public static IReadOnlyList<(int Frequency, int Duration)> GetRepeatedSequence(BeepType type, int repeatCount)
	{
		var single = GetDefaultSequence(type);
		repeatCount = ClampRepeatCount(repeatCount);
		if (repeatCount == 1)
			return single;

		var sequence = new List<(int Frequency, int Duration)>(single.Count * repeatCount);
		for (var i = 0; i < repeatCount; i++)
			sequence.AddRange(single);
		return sequence;
	}

	public static IReadOnlyList<(int Frequency, int Duration)> GetDefaultSequence(BeepType type) => type switch
	{
		BeepType.Start => new[] { (DefaultStartFrequency, DefaultStartDuration) },
		BeepType.Success => DefaultSuccessSequence,
		BeepType.Failure => Enumerable.Repeat((DefaultFailureFrequency, DefaultFailureDuration), DefaultFailureRepeats).ToArray(),
		BeepType.End => new[] { (DefaultEndFrequency, DefaultEndDuration) },
		BeepType.Mute => new[] { (DefaultMuteFrequency, DefaultMuteDuration) },
		BeepType.Unmute => new[] { (DefaultUnmuteFrequency, DefaultUnmuteDuration) },
		BeepType.Waiting => DefaultWaitingSequence,
		_ => throw new ArgumentOutOfRangeException(nameof(type))
	};

	/// <summary>
	/// Plays an arbitrary .wav file for previewing (e.g. from the settings dialog), independent
	/// of the UseCustomBeeps toggle and the clips loaded for each beep type. Expects an
	/// already-resolved file path (see <c>CustomBeepSettingsData.ResolveAudioFilePath</c>).
	/// Returns true if the file was read and handed over; false if it is missing or unreadable.
	/// </summary>
	public static bool PreviewFile(string? resolvedFilePath)
	{
		if (string.IsNullOrWhiteSpace(resolvedFilePath) || !File.Exists(resolvedFilePath))
			return false;

		lock (SyncLock)
		{
			try
			{
				var clip = BeepClipReader.ReadFile(resolvedFilePath, BeepAudioOutput.SampleRate, BeepAudioOutput.Channels);
				Output().Play(clip);
				return true;
			}
			catch
			{
				return false;
			}
		}
	}

	/// <summary>
	/// Releases the audio device and everything loaded for it. Stays public because the UI
	/// project calls it on window close; every sibling that touches these statics holds
	/// <c>SyncLock</c>, and so does this. A beep after this point simply opens a new device.
	/// </summary>
	public static void DisposePlayers()
	{
		lock (SyncLock)
		{
			_customClips.Clear();
			_defaultClips.Clear();
			_output?.Dispose();
			_output = null;
		}
	}
}
