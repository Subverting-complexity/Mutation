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
	private const int MaxSessions = AppConstants.MaxSpeechSessions;
	private static readonly TimeSpan SessionRetryDelay = TimeSpan.FromSeconds(1);
	private static readonly Regex SessionFilePattern = new(
			  "^session_(\\d{4}-\\d{2}-\\d{2}_\\d{2}-\\d{2}-\\d{2})\\.[A-Za-z0-9]+$",
			  RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	private readonly Settings _settings;
	private readonly SpeechToTextState _state;
	private CognitiveSupport.AudioRecorder? _audioRecorder;
	private readonly object _sessionLock = new();
	private SpeechSession? _currentRecordingSession;

	public SpeechToTextManager(Settings settings)
	{
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
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

	public async Task<SpeechSession> StartRecordingAsync(int microphoneDeviceIndex)
	{
		EnsureSessionsDirectory();
		string path = await CreateSessionFileAsync(".ogg").ConfigureAwait(false);
		if (!TryCreateSession(path, out var session))
			throw new InvalidOperationException($"Generated session path '{Path.GetFileName(path)}' could not be parsed.");

		await _state.AudioRecorderLock.WaitAsync().ConfigureAwait(false);
		try
		{
			lock (_sessionLock)
			{
				_currentRecordingSession = session;
			}

			_audioRecorder = new CognitiveSupport.AudioRecorder();
			_audioRecorder.StartRecording(microphoneDeviceIndex, path);
		}
		finally
		{
			_state.AudioRecorderLock.Release();
		}

		return session;
	}

	public async Task<string> StopRecordingAndTranscribeAsync(ISpeechToTextService service, string prompt, CancellationToken token)
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
				_audioRecorder?.Dispose();
				_audioRecorder = null;

				transcribeToken.ThrowIfCancellationRequested();

				return await service.ConvertAudioToText(prompt, recordingSession.FilePath, transcribeToken).ConfigureAwait(false);
			}
			finally
			{
				_state.StopTranscription();
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
				_state.StopTranscription();
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
		_state.StopTranscription();
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

		// If this is a video file, convert to OGG Opus format
		if (AudioFileConverter.IsVideoFile(sourcePath))
		{
			string destinationPath = await CreateSessionFileAsync(".ogg").ConfigureAwait(false);
			await Task.Run(() => AudioFileConverter.ConvertMp4ToOgg(sourcePath, destinationPath), token).ConfigureAwait(false);

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

			sessions.Sort((left, right) => right.Timestamp.CompareTo(left.Timestamp));

			int currentCount = sessions.Count;
			if (currentCount <= MaxSessions)
				return;

			foreach (var session in sessions.OrderBy(s => s.Timestamp))
			{
				if (currentCount <= MaxSessions)
					break;

				if (exclusions.Contains(session.FilePath))
					continue;

				try
				{
					File.Delete(session.FilePath);
					currentCount--;
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
