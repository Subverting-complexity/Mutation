using System.Media;

namespace CognitiveSupport;

public enum BeepType { Start, Success, Failure, End, Mute, Unmute }

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

	// Upper bound on how many times a beep is repeated in one PlayRepeated call. Keeps a
	// runaway retry count from synthesizing a multi-second WAV the user has to sit through.
	public const int MaxRepeatCount = 10;

	private static readonly object SyncLock = new();
	private static readonly TimeSpan DuplicateSuppressWindow = TimeSpan.FromMilliseconds(500);
	private static readonly Dictionary<BeepType, DateTime> _lastPlayed = new();
	private static SoundPlayer? _playerStart;
	private static SoundPlayer? _playerSuccess;
	private static SoundPlayer? _playerFailure;
	private static SoundPlayer? _playerEnd;
	private static SoundPlayer? _playerMute;
	private static SoundPlayer? _playerUnmute;
	private static SoundPlayer? _previewPlayer;
	// Bumped whenever the per-type players are torn down, so an in-flight repeat loop
	// can tell that the SoundPlayer it captured is no longer the live one.
	private static int _playerGeneration;
	private static readonly Dictionary<(BeepType Type, int RepeatCount), (SoundPlayer Player, MemoryStream Stream)> _defaultPlayers = new();
	public static IReadOnlyList<string> LastInitializationIssues { get; private set; } = Array.Empty<string>();

	public static void Initialize(Settings settings)
	{
		lock (SyncLock)
		{
			DisposePlayers();
			var issues = new List<string>();
			var custom = settings.AudioSettings?.CustomBeepSettings;
			if (custom?.UseCustomBeeps == true)
			{
				_playerStart = LoadPlayer(custom.ResolveAudioFilePath(custom.BeepStartFile ?? string.Empty), fp => issues.Add($"Could not load start beep file: {fp}"));
				_playerSuccess = LoadPlayer(custom.ResolveAudioFilePath(custom.BeepSuccessFile ?? string.Empty), fp => issues.Add($"Could not load success beep file: {fp}"));
				_playerFailure = LoadPlayer(custom.ResolveAudioFilePath(custom.BeepFailureFile ?? string.Empty), fp => issues.Add($"Could not load failure beep file: {fp}"));
				_playerEnd = LoadPlayer(custom.ResolveAudioFilePath(custom.BeepEndFile ?? string.Empty), fp => issues.Add($"Could not load end beep file: {fp}"));
				_playerMute = LoadPlayer(custom.ResolveAudioFilePath(custom.BeepMuteFile ?? string.Empty), fp => issues.Add($"Could not load mute beep file: {fp}"));
				_playerUnmute = LoadPlayer(custom.ResolveAudioFilePath(custom.BeepUnmuteFile ?? string.Empty), fp => issues.Add($"Could not load unmute beep file: {fp}"));
			}
			LastInitializationIssues = issues;
		}
	}

	private static SoundPlayer? LoadPlayer(string filePath, Action<string> onError)
	{
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
			return null;

		try
		{
			var player = new SoundPlayer(filePath);
			player.Load();
			return player;
		}
		catch
		{
			onError(filePath);
			return null;
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
		}
		if (TryPlayCustom(type))
			return;
		PlayDefault(type, repeatCount: 1);
	}

	// Plays <paramref name="repeatCount"/> copies of a beep as a single sound so the
	// listener can actually count them. Playing Play() in a loop does not work: the
	// duplicate-suppression window above swallows every call after the first (issue
	// #216), and even without it SoundPlayer.Play restarts rather than queues. The
	// default beeps are therefore synthesized as one N-tone WAV, and custom beep files
	// are played back-to-back synchronously off the calling thread.
	public static void PlayRepeated(BeepType type, int repeatCount)
	{
		repeatCount = ClampRepeatCount(repeatCount);

		lock (SyncLock)
		{
			// Deliberately not consulting the suppression window: this call *is* the
			// repeat, so it must never be collapsed. Still stamped so a stray single
			// Play right behind it stays suppressed.
			_lastPlayed[type] = DateTime.UtcNow;
		}

		if (TryPlayCustomRepeated(type, repeatCount))
			return;
		PlayDefault(type, repeatCount);
	}

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

	private static bool TryPlayCustomRepeated(BeepType type, int repeatCount)
	{
		var player = GetCustomPlayer(type);
		if (player is null)
			return false;

		if (repeatCount == 1)
		{
			player.Play();
			return true;
		}

		// PlaySync blocks, so the repeat runs off the caller's thread rather than
		// stalling the transcription retry it is reporting on. That leaves the loop
		// running while Initialize/DisposePlayers may replace these players (a Settings
		// save, or shutdown), so it bails out the moment its generation is superseded
		// instead of hammering a disposed SoundPlayer.
		int generation = Volatile.Read(ref _playerGeneration);
		Task.Run(() =>
		{
			for (var i = 0; i < repeatCount; i++)
			{
				if (Volatile.Read(ref _playerGeneration) != generation)
					return;

				try
				{
					player.PlaySync();
				}
				catch
				{
					// A failed beep must never take down the operation it reports on.
					return;
				}
			}
		});
		return true;
	}

	private static bool TryPlayCustom(BeepType type)
	{
		var player = GetCustomPlayer(type);
		if (player != null)
		{
			player.Play();
			return true;
		}
		return false;
	}

	private static SoundPlayer? GetCustomPlayer(BeepType type) => type switch
	{
		BeepType.Start => _playerStart,
		BeepType.Success => _playerSuccess,
		BeepType.Failure => _playerFailure,
		BeepType.End => _playerEnd,
		BeepType.Mute => _playerMute,
		BeepType.Unmute => _playerUnmute,
		_ => null
	};

	// Default beeps are synthesized once into in-memory WAVs and played through
	// SoundPlayer.Play (asynchronous), so they never block the calling thread the
	// way Console.Beep did (issue #169).
	private static void PlayDefault(BeepType type, int repeatCount)
	{
		SoundPlayer player;
		var key = (type, ClampRepeatCount(repeatCount));
		lock (SyncLock)
		{
			if (!_defaultPlayers.TryGetValue(key, out var cached))
			{
				var wav = BeepToneSynthesizer.SynthesizeWav(GetRepeatedSequence(key.Item1, key.Item2));
				var stream = new MemoryStream(wav, writable: false);
				cached = (new SoundPlayer(stream), stream);
				cached.Player.Load();
				_defaultPlayers[key] = cached;
			}
			player = cached.Player;
		}
		player.Play();
	}

	public static IReadOnlyList<(int Frequency, int Duration)> GetDefaultSequence(BeepType type) => type switch
	{
		BeepType.Start => new[] { (DefaultStartFrequency, DefaultStartDuration) },
		BeepType.Success => DefaultSuccessSequence,
		BeepType.Failure => Enumerable.Repeat((DefaultFailureFrequency, DefaultFailureDuration), DefaultFailureRepeats).ToArray(),
		BeepType.End => new[] { (DefaultEndFrequency, DefaultEndDuration) },
		BeepType.Mute => new[] { (DefaultMuteFrequency, DefaultMuteDuration) },
		BeepType.Unmute => new[] { (DefaultUnmuteFrequency, DefaultUnmuteDuration) },
		_ => throw new ArgumentOutOfRangeException(nameof(type))
	};

	// Plays an arbitrary .wav file for previewing (e.g. from the settings dialog),
	// independent of the UseCustomBeeps toggle and the cached per-type players.
	// Expects an already-resolved file path (see CustomBeepSettingsData.ResolveAudioFilePath).
	// Returns true if playback started; false if the file is missing or could not be loaded.
	public static bool PreviewFile(string? resolvedFilePath)
	{
		if (string.IsNullOrWhiteSpace(resolvedFilePath) || !File.Exists(resolvedFilePath))
			return false;

		lock (SyncLock)
		{
			try
			{
				_previewPlayer?.Dispose();
				_previewPlayer = new SoundPlayer(resolvedFilePath);
				_previewPlayer.Load();
				_previewPlayer.Play();
				return true;
			}
			catch
			{
				_previewPlayer?.Dispose();
				_previewPlayer = null;
				return false;
			}
		}
	}

	public static void DisposePlayers()
	{
		// Signal before disposing, so a repeat loop stops rather than racing the tear-down.
		Interlocked.Increment(ref _playerGeneration);
		_previewPlayer?.Dispose();
		_previewPlayer = null;
		_playerStart?.Dispose();
		_playerSuccess?.Dispose();
		_playerFailure?.Dispose();
		_playerEnd?.Dispose();
		_playerMute?.Dispose();
		_playerUnmute?.Dispose();
		_playerStart = null;
		_playerSuccess = null;
		_playerFailure = null;
		_playerEnd = null;
		_playerMute = null;
		_playerUnmute = null;
		foreach (var (player, stream) in _defaultPlayers.Values)
		{
			player.Dispose();
			stream.Dispose();
		}
		_defaultPlayers.Clear();
	}
}
