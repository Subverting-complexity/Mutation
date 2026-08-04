using CognitiveSupport;
using Mutation.Ui.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mutation.Ui;

public class SpeechToTextManager : IDisposable
{
	private const string SessionFilePrefix = "session_";
	private const string SessionTimestampFormat = "yyyy-MM-dd_HH-mm-ss";
	private static readonly TimeSpan SessionRetryDelay = TimeSpan.FromSeconds(1);
	private static readonly Regex SessionFilePattern = new(
			  "^session_(\\d{4}-\\d{2}-\\d{2}_\\d{2}-\\d{2}-\\d{2})\\.[A-Za-z0-9]+$",
			  RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	private readonly Settings _settings;
	private readonly SpeechToTextState _state;
	private readonly Func<IAudioRecorder> _recorderFactory;
	private IAudioRecorder? _audioRecorder;
	private readonly object _sessionLock = new();
	private SpeechSession? _currentRecordingSession;

	public SpeechToTextManager(Settings settings)
		: this(settings, () => new CognitiveSupport.AudioRecorder())
	{
	}

	// Test seam: injects the recorder so a start can be made to fail on demand
	// without real NAudio hardware.
	internal SpeechToTextManager(Settings settings, Func<IAudioRecorder> recorderFactory)
	{
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_recorderFactory = recorderFactory ?? throw new ArgumentNullException(nameof(recorderFactory));
		_state = new SpeechToTextState(() => _audioRecorder);
	}

	public bool Recording => _state.RecordingAudio;
	public bool Transcribing => _state.TranscribingAudio;

	public SpeechSession? CurrentRecordingSession
	{
		get
		{
			lock (_sessionLock)
			{
				return _currentRecordingSession;
			}
		}
	}

	private string SessionsDirectory => Path.Combine(_settings.SpeechToTextSettings!.TempDirectory!, Constants.SessionsDirectoryName);

	// Minimum trimmed speech below which we treat a recording as "no speech detected".
	private const double MinSpeechSecondsForTranscription = 0.25;

	private SilenceTrimmerOptions? BuildSilenceOptions()
	{
		var stt = _settings.SpeechToTextSettings;
		if (stt is null || !stt.EnableSilenceStripping)
			return null;

		return new SilenceTrimmerOptions(
			stt.SilenceThresholdDbFs,
			stt.MinSilenceSeconds,
			stt.SilenceGuardMilliseconds);
	}

	public bool HasRecordedAudio()
	{
		foreach (var session in GetSessions())
		{
			if (TryGetFileLength(session.FilePath, out var length) && length > 0)
				return true;
		}

		return false;
	}

	public bool TryGetLatestRecording(out string path)
	{
		foreach (var session in GetSessions())
		{
			if (TryGetFileLength(session.FilePath, out var length) && length > 0)
			{
				path = session.FilePath;
				return true;
			}
		}

		path = string.Empty;
		return false;
	}

	public IReadOnlyList<SpeechSession> GetSessions()
	{
		lock (_sessionLock)
		{
			try
			{
				if (!Directory.Exists(SessionsDirectory))
					return Array.Empty<SpeechSession>();

				var sessions = new List<SpeechSession>();
				foreach (var file in Directory.EnumerateFiles(SessionsDirectory))
				{
					if (TryCreateSession(file, out var session))
						sessions.Add(session);
				}

				sessions.Sort((left, right) => right.Timestamp.CompareTo(left.Timestamp));

				if (_currentRecordingSession != null && !sessions.Any(s => PathsEqual(s.FilePath, _currentRecordingSession.FilePath)))
					sessions.Insert(0, _currentRecordingSession);

				return sessions.ToArray();
			}
			catch (DirectoryNotFoundException)
			{
				return Array.Empty<SpeechSession>();
			}
			catch (UnauthorizedAccessException)
			{
				return Array.Empty<SpeechSession>();
			}
			catch (PathTooLongException)
			{
				return Array.Empty<SpeechSession>();
			}
			catch (IOException)
			{
				return Array.Empty<SpeechSession>();
			}

		}
	}

	/// <summary>
	/// Bounded wait for the recorder lock when starting a recording. A previous
	/// transcription can hold the lock across its whole network call, so an
	/// unbounded wait would leave the app claiming to listen while capturing
	/// nothing. Tests inject a shorter value via <see cref="StartRecordingAsync(int, TimeSpan?)"/>.
	/// </summary>
	private static readonly TimeSpan DefaultStartRecordingLockTimeout = TimeSpan.FromSeconds(5);

	public async Task<SpeechSession> StartRecordingAsync(int microphoneDeviceIndex, TimeSpan? lockTimeout = null)
	{
		bool acquired = await _state.AudioRecorderLock
			.WaitAsync(lockTimeout ?? DefaultStartRecordingLockTimeout)
			.ConfigureAwait(false);
		if (!acquired)
			throw new RecorderBusyException("The previous operation is still finishing; recording could not start.");

		try
		{
			// Reject a second start while a recorder is already live. Overwriting
			// the field would orphan the running capture — its mic is never
			// released and its .ogg never finalized. Guard before creating the
			// session file so a rejected entry leaves nothing behind.
			if (_audioRecorder != null)
				throw new InvalidOperationException("A recording is already in progress.");

			// Create the session file only after winning the lock so a busy
			// timeout leaves no orphaned empty session in the history.
			EnsureSessionsDirectory();
			string path = await CreateSessionFileAsync(".ogg").ConfigureAwait(false);
			if (!TryCreateSession(path, out var session))
				throw new InvalidOperationException($"Generated session path '{Path.GetFileName(path)}' could not be parsed.");

			lock (_sessionLock)
			{
				_currentRecordingSession = session;
			}

			// Start into a local first; commit the field only once StartRecording
			// succeeds. A failed start (device unplugged, NAudio error) must not
			// leave the manager reporting "recording" with no active capture.
			IAudioRecorder recorder = _recorderFactory();
			try
			{
				recorder.StartRecording(microphoneDeviceIndex, path, BuildSilenceOptions());
			}
			catch
			{
				recorder.Dispose();
				lock (_sessionLock)
				{
					_currentRecordingSession = null;
				}
				TryDeleteSessionFile(path);
				throw;
			}

			_audioRecorder = recorder;
			return session;
		}
		finally
		{
			_state.AudioRecorderLock.Release();
		}
	}

	/// <param name="onRecordingStopped">
	/// Raised once the recorder has been stopped and disposed, before anything that can
	/// throw and before the transcription request. That is the only point at which
	/// "capture has ended" is true and the microphone is closed, so a caller can signal
	/// it audibly without the sound bleeding into the recording it is ending.
	/// </param>
	public async Task<string> StopRecordingAndTranscribeAsync(ISpeechToTextService service, string prompt, CancellationToken token, Action? onRecordingStopped = null)
	{
		if (service is null)
			throw new ArgumentNullException(nameof(service));

		SpeechSession? recordingSession;
		lock (_sessionLock)
		{
			recordingSession = _currentRecordingSession;
		}

		if (recordingSession is null)
			throw new InvalidOperationException("No recording is currently in progress.");

		await _state.AudioRecorderLock.WaitAsync(token).ConfigureAwait(false);
		try
		{
			var transcribeToken = _state.StartTranscription(token);
			try
			{
				_audioRecorder?.StopRecording();
				double? trimmedSpeechSeconds = _audioRecorder?.TrimmedSpeechSeconds;
				Exception? captureError = _audioRecorder?.CaptureException;
				_audioRecorder?.Dispose();
				_audioRecorder = null;

				// Before the cancellation and capture-error checks below: capture has
				// ended however this call turns out, so the end-of-recording signal is
				// owed to the user even when there is nothing left to transcribe.
				NotifyRecordingStopped(onRecordingStopped);

				transcribeToken.ThrowIfCancellationRequested();

				// A failure on the capture thread was deferred to here so it surfaces as a
				// normal error (handled by AudioSessionManager) instead of crashing.
				if (captureError is not null)
					throw new InvalidOperationException("Audio capture failed during recording.", captureError);

				// When silence stripping ran and left almost no speech, skip the API call.
				if (trimmedSpeechSeconds is double seconds && seconds < MinSpeechSecondsForTranscription)
					throw new NoSpeechDetectedException("No speech detected after trimming silence.");

				int liveTimeout = _settings.SpeechToTextSettings?.LiveTranscriptionTimeoutSeconds ?? 60;
				return await service.ConvertAudioToText(prompt, recordingSession.FilePath, transcribeToken, liveTimeout).ConfigureAwait(false);
			}
			finally
			{
				_state.EndTranscription();
			}
		}
		finally
		{
			lock (_sessionLock)
			{
				_currentRecordingSession = null;
			}

			_state.AudioRecorderLock.Release();
		}
	}

	// A notification is a courtesy to the caller, never a reason to fail the
	// transcription the user is actually waiting on.
	private static void NotifyRecordingStopped(Action? onRecordingStopped)
	{
		if (onRecordingStopped is null)
			return;

		try
		{
			onRecordingStopped();
		}
		catch (Exception ex)
		{
			ErrorLogger.LogError("End-of-recording notification", ex);
		}
	}

	public async Task<string> TranscribeExistingRecordingAsync(ISpeechToTextService service, SpeechSession session, string prompt, CancellationToken token)
	{
		if (service is null)
			throw new ArgumentNullException(nameof(service));
		if (session is null)
			throw new ArgumentNullException(nameof(session));

		await _state.AudioRecorderLock.WaitAsync(token).ConfigureAwait(false);
		try
		{
			if (_audioRecorder != null)
				throw new InvalidOperationException("Recording is currently in progress.");

			if (!File.Exists(session.FilePath))
				throw new FileNotFoundException("Selected session file is missing.", session.FilePath);

			string text = string.Empty;
			var transcribeToken = _state.StartTranscription(token);
			try
			{
				int timeout = _settings.SpeechToTextSettings?.FileTranscriptionTimeoutSeconds ?? 300;
				text = await service.ConvertAudioToText(prompt, session.FilePath, transcribeToken, timeout).ConfigureAwait(false);
			}
			finally
			{
				_state.EndTranscription();
			}

			return text;
		}
		finally
		{
			_state.AudioRecorderLock.Release();
		}
	}

	public void CancelTranscription()
	{
		_state.CancelTranscription();
	}

	public async Task StopRecordingAsync()
	{
		await _state.AudioRecorderLock.WaitAsync().ConfigureAwait(false);
		try
		{
			_audioRecorder?.StopRecording();
			_audioRecorder?.Dispose();
			_audioRecorder = null;
		}
		finally
		{
			lock (_sessionLock)
			{
				_currentRecordingSession = null;
			}

			_state.AudioRecorderLock.Release();
		}
	}

	public async Task<SpeechSession> ImportUploadedAudioAsync(string sourcePath, CancellationToken token)
	{
		if (string.IsNullOrWhiteSpace(sourcePath))
			throw new ArgumentException("Source path is required.", nameof(sourcePath));

		EnsureSessionsDirectory();

		string extension = Path.GetExtension(sourcePath);
		if (string.IsNullOrWhiteSpace(extension))
			throw new InvalidOperationException("Uploaded audio must have a file extension.");

		var silenceOptions = BuildSilenceOptions();

		// Video files always need decoding; audio files are decoded too when silence
		// stripping is enabled so their silent gaps get trimmed (otherwise copied as-is).
		if (AudioFileConverter.IsVideoFile(sourcePath) || silenceOptions is not null)
		{
			string destinationPath = await CreateSessionFileAsync(".ogg").ConfigureAwait(false);
			await Task.Run(() => AudioFileConverter.ConvertToOgg(sourcePath, destinationPath, silenceOptions), token).ConfigureAwait(false);

			if (!TryCreateSession(destinationPath, out var convertedSession))
				throw new InvalidOperationException($"Unable to parse converted session '{Path.GetFileName(destinationPath)}'.");

			return convertedSession;
		}

		// For audio files, copy directly
		string audioDestinationPath = await CreateSessionFileAsync(extension).ConfigureAwait(false);
		await CopyFileAsync(sourcePath, audioDestinationPath, token).ConfigureAwait(false);

		if (!TryCreateSession(audioDestinationPath, out var session))
			throw new InvalidOperationException($"Unable to parse uploaded session '{Path.GetFileName(audioDestinationPath)}'.");

		return session;
	}

	public async Task<SpeechSession> DuplicateSessionAsync(SpeechSession sourceSession, CancellationToken token)
	{
		if (sourceSession is null)
			throw new ArgumentNullException(nameof(sourceSession));

		await Task.Delay(SessionRetryDelay, token).ConfigureAwait(false);

		string destinationPath = await CreateSessionFileAsync(sourceSession.Extension).ConfigureAwait(false);
		await CopyFileAsync(sourceSession.FilePath, destinationPath, token).ConfigureAwait(false);

		if (!TryCreateSession(destinationPath, out var session))
			throw new InvalidOperationException($"Unable to parse duplicated session '{Path.GetFileName(destinationPath)}'.");

		return session;
	}

	public Task CleanupSessionsAsync(IEnumerable<string> exclusionPaths)
	{
		if (exclusionPaths is null)
			exclusionPaths = Array.Empty<string>();

		var exclusions = new HashSet<string>(exclusionPaths.Where(p => !string.IsNullOrWhiteSpace(p)), StringComparer.OrdinalIgnoreCase);

		lock (_sessionLock)
		{
			if (_currentRecordingSession != null)
				exclusions.Add(_currentRecordingSession.FilePath);
		}

		return Task.Run(() => CleanupSessionsInternal(exclusions));
	}

	private void CleanupSessionsInternal(HashSet<string> exclusions)
	{
		try
		{
			if (!Directory.Exists(SessionsDirectory))
				return;

			var sessions = new List<SpeechSession>();
			foreach (var file in Directory.EnumerateFiles(SessionsDirectory))
			{
				if (TryCreateSession(file, out var session))
					sessions.Add(session);
			}

			// Read the retention cap at cleanup time (clamped defensively) so a changed
			// setting takes effect without an app restart, and a hand-edited out-of-range
			// JSON value cannot break cleanup.
			int maxRetained = SpeechToTextSettings.ClampRetainedSessions(
				_settings.SpeechToTextSettings?.MaxRetainedSessions
				?? SpeechToTextSettings.DefaultMaxRetainedSessions);

			foreach (var session in SessionRetentionPlanner.SelectSessionsToDelete(sessions, exclusions, maxRetained))
			{
				try
				{
					File.Delete(session.FilePath);
				}
				catch (DirectoryNotFoundException)
				{
					continue;
				}
				catch (UnauthorizedAccessException)
				{
					continue;
				}
				catch (PathTooLongException)
				{
					continue;
				}
				catch (IOException)
				{
					continue;
				}
			}
		}
		catch (DirectoryNotFoundException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
		catch (PathTooLongException)
		{
		}
		catch (IOException)
		{
			// Best-effort cleanup.
		}
	}

	private void EnsureSessionsDirectory()
	{
		Directory.CreateDirectory(SessionsDirectory);
	}

	private async Task<string> CreateSessionFileAsync(string extension)
	{
		if (string.IsNullOrWhiteSpace(extension))
			throw new ArgumentException("Extension cannot be empty.", nameof(extension));

		extension = NormalizeExtension(extension);

		for (int attempt = 0; attempt < 3; attempt++)
		{
			string timestamp = DateTime.Now.ToString(SessionTimestampFormat, CultureInfo.InvariantCulture);
			string fileName = $"{SessionFilePrefix}{timestamp}{extension}";
			string path = Path.Combine(SessionsDirectory, fileName);

			try
			{
				using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				}
				return path;
			}
			catch (UnauthorizedAccessException)
			{
				throw;
			}
			catch (DirectoryNotFoundException)
			{
				throw;
			}
			catch (PathTooLongException)
			{
				throw;
			}
			catch (IOException)
			{
				if (attempt == 2)
					throw;

				await Task.Delay(SessionRetryDelay).ConfigureAwait(false);
			}

		}

		throw new IOException("Failed to create a unique session file name.");
	}

	// Best-effort removal of a just-created session file whose recording never
	// started, so a failed start leaves no empty session lingering in history.
	private static void TryDeleteSessionFile(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (DirectoryNotFoundException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
		catch (PathTooLongException)
		{
		}
		catch (IOException)
		{
		}
	}

	private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken token)
	{
		await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
		await source.CopyToAsync(destination, 81920, token).ConfigureAwait(false);
	}

	private static bool TryCreateSession(string filePath, out SpeechSession session)
	{
		session = null!;
		string fileName = Path.GetFileName(filePath);
		var match = SessionFilePattern.Match(fileName);
		if (!match.Success)
			return false;

		string timestampText = match.Groups[1].Value;
		if (!DateTime.TryParseExact(timestampText, SessionTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
			return false;

		session = new SpeechSession(filePath, timestamp);
		return true;
	}

	private static string NormalizeExtension(string extension)
	{
		return extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}";
	}

	private static bool TryGetFileLength(string path, out long length)
	{
		length = 0;
		try
		{
			var info = new FileInfo(path);
			if (!info.Exists)
				return false;

			length = info.Length;
			return true;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
		catch (DirectoryNotFoundException)
		{
			return false;
		}
		catch (PathTooLongException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private static bool PathsEqual(string left, string right) =>
			  string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	public void Dispose()
	{
		_state.Dispose();
		_audioRecorder?.Dispose();
	}
}
