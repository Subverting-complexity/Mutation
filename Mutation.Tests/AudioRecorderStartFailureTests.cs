using System;
using System.Collections.Generic;
using System.IO;
using CognitiveSupport;
using NAudio.Wave;

namespace Mutation.Tests;

/// <summary>
/// Covers what <see cref="AudioRecorder.StartRecording"/> releases when a start fails part way
/// through. The case that matters is a second start on a recorder whose first recording was
/// never stopped: that recording's resources are still committed, so "is the field empty?"
/// cannot tell whether the new ones were handed over.
/// </summary>
public class AudioRecorderStartFailureTests : IDisposable
{
	private readonly string _workingDirectory = Directory.CreateTempSubdirectory("audio-recorder-start-tests").FullName;

	public void Dispose()
	{
		try { Directory.Delete(_workingDirectory, recursive: true); } catch { }
	}

	[Fact]
	public void A_failed_start_over_a_recording_that_was_never_stopped_closes_its_new_file()
	{
		string firstPath = Path.Combine(_workingDirectory, "first.ogg");
		string secondPath = Path.Combine(_workingDirectory, "second.ogg");
		var sources = new Queue<IAudioCaptureSource>(new IAudioCaptureSource[]
		{
			new StubCaptureSource(),
			new StubCaptureSource { FailToSubscribe = true }
		});

		using var recorder = new AudioRecorder(_ => sources.Dequeue());
		recorder.StartRecording(0, firstPath);

		Assert.Throws<InvalidOperationException>(() => recorder.StartRecording(0, secondPath));

		// The failed start opened this file for writing. Left open, it stays locked until a
		// finalizer happens to run, and nothing can delete or reuse it.
		using var reopened = new FileStream(secondPath, FileMode.Open, FileAccess.Read, FileShare.None);
	}

	/// <summary>
	/// A capture source that never delivers audio. <see cref="FailToSubscribe"/> makes the
	/// recorder's handler subscription throw, which fails the start after its file and encoder
	/// exist but before they are committed.
	/// </summary>
	private sealed class StubCaptureSource : IAudioCaptureSource
	{
		private EventHandler<WaveInEventArgs>? _dataAvailable;

		public bool FailToSubscribe { get; init; }

		public event EventHandler<WaveInEventArgs>? DataAvailable
		{
			add
			{
				if (FailToSubscribe)
					throw new InvalidOperationException("Scripted start failure.");

				_dataAvailable += value;
			}
			remove => _dataAvailable -= value;
		}

		public event EventHandler<StoppedEventArgs>? RecordingStopped;

		public void StartRecording()
		{
		}

		public void StopRecording() => RecordingStopped?.Invoke(this, new StoppedEventArgs());

		public void Dispose()
		{
		}
	}
}
