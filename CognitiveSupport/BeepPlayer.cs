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
		PlayDefault(type);
	}

	private static bool TryPlayCustom(BeepType type)
	{
		var player = type switch
		{
			BeepType.Start => _playerStart,
			BeepType.Success => _playerSuccess,
			BeepType.Failure => _playerFailure,
			BeepType.End => _playerEnd,
			BeepType.Mute => _playerMute,
			BeepType.Unmute => _playerUnmute,
			_ => null
		};
		if (player != null)
		{
			player.Play();
			return true;
		}
		return false;
	}

	private static void PlayDefault(BeepType type)
	{
		switch (type)
		{
			case BeepType.Start:
				Console.Beep(DefaultStartFrequency, DefaultStartDuration);
				break;
			case BeepType.Success:
				foreach (var (frequency, duration) in DefaultSuccessSequence)
					Console.Beep(frequency, duration);
				break;
			case BeepType.Failure:
				for (var i = 0; i < DefaultFailureRepeats; i++)
					Console.Beep(DefaultFailureFrequency, DefaultFailureDuration);
				break;
			case BeepType.End:
				Console.Beep(DefaultEndFrequency, DefaultEndDuration);
				break;
			case BeepType.Mute:
				Console.Beep(DefaultMuteFrequency, DefaultMuteDuration);
				break;
			case BeepType.Unmute:
				Console.Beep(DefaultUnmuteFrequency, DefaultUnmuteDuration);
				break;
		}
	}

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
	}
}
