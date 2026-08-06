using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;
using NAudio.Wave;

namespace Mutation.Tests;

/// <summary>
/// Covers what <see cref="AudioRecorder"/> does between "stop" being asked for and the Ogg
/// stream being closed. NAudio's <c>StopRecording</c> only signals its capture thread — buffers
/// it has already filled arrive afterwards — so closing the encoder straight away threw the tail
/// of every recording away. The capture device is faked, both because the build machine has no
/// microphone and because the ordering under test only reproduces reliably when it is scripted.
/// </summary>
public class AudioRecorderDrainTests : IDisposable
{
	private const int SampleRate = 48000;
	private const int SamplesPerFrame = 960; // 20 ms, the Opus frame size the recorder uses

	private readonly string _workingDirectory = Directory.CreateTempSubdirectory("audio-recorder-tests").FullName;

	public void Dispose()
	{
		try { Directory.Delete(_workingDirectory, recursive: true); } catch { }
	}

	[Fact]
	public void Buffers_delivered_before_stop_are_encoded()
	{
		using var capture = new FakeCaptureSource();
		string path = Path.Combine(_workingDirectory, "before-stop.ogg");

		using (var recorder = new AudioRecorder(_ => capture))
		{
			recorder.StartRecording(0, path);
			capture.Deliver(Tone(frames: 25));
			capture.ReleaseDrain();
			recorder.StopRecording();

			Assert.Null(recorder.CaptureException);
		}

		Assert.InRange(DecodedFrames(path), 24, 26);
	}

	[Fact]
	public async Task Buffers_delivered_after_stop_is_signalled_are_still_encoded()
	{
		using var capture = new FakeCaptureSource
		{
			// Stands in for the ~300 ms NAudio still has queued when the user releases the
			// hotkey: three 100 ms buffers, delivered on the capture thread after the stop.
			BufferDeliveredWhileDraining = Tone(frames: 15)
		};
		string path = Path.Combine(_workingDirectory, "after-stop.ogg");

		using (var recorder = new AudioRecorder(_ => capture))
		{
			recorder.StartRecording(0, path);
			capture.Deliver(Tone(frames: 5));

			// StopRecording blocks until the capture source has drained, so it runs off the
			// test thread — which is what releases the drain.
			Task stopping = Task.Run(recorder.StopRecording);
			Assert.True(capture.StopSignalled.Wait(TimeSpan.FromSeconds(10)));
			capture.ReleaseDrain();
			await stopping.WaitAsync(TimeSpan.FromSeconds(10));

			Assert.Null(recorder.CaptureException);
		}

		// All 20 frames, not just the 5 that arrived before the stop.
		Assert.InRange(DecodedFrames(path), 19, 21);
	}

	[Fact]
	public void Stop_gives_up_on_a_capture_source_that_never_reports_it_has_drained()
	{
		// A wedged driver must cost a bounded pause, not a hung app: the recording is still
		// closed and still readable, it just loses whatever the driver never handed over.
		using var capture = new FakeCaptureSource { NeverSignalsDrained = true };
		string path = Path.Combine(_workingDirectory, "wedged.ogg");

		using (var recorder = new AudioRecorder(_ => capture))
		{
			recorder.StartRecording(0, path);
			capture.Deliver(Tone(frames: 10));
			recorder.StopRecording();
		}

		Assert.InRange(DecodedFrames(path), 9, 11);
	}

	private static byte[] Tone(int frames)
	{
		var pcm = new byte[frames * SamplesPerFrame * 2];
		for (int i = 0; i < frames * SamplesPerFrame; i++)
		{
			// A 440 Hz sine, so the encoder has real signal to work with rather than silence.
			short sample = (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / SampleRate));
			pcm[i * 2] = (byte)(sample & 0xFF);
			pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
		}

		return pcm;
	}

	// Decoded 20 ms frames in the finished recording. Opus does not round-trip an exact sample
	// count — the encoder's pre-skip is dropped on decode and a partial final frame is not
	// written — so callers assert a frame or so either side.
	private static int DecodedFrames(string path)
	{
		int samples = 0;
		OggOpusPcmDecoder.DecodeToMonoPcm(path, frame => samples += frame.Length);
		return (int)Math.Round(samples / (double)SamplesPerFrame);
	}

	/// <summary>
	/// A scripted stand-in for NAudio's <c>WaveInEvent</c>. <see cref="StopRecording"/> returns
	/// immediately, as the real one does, and the remaining buffers plus
	/// <see cref="RecordingStopped"/> are delivered from a separate thread that the test lets
	/// through when it chooses.
	/// </summary>
	private sealed class FakeCaptureSource : IAudioCaptureSource
	{
		private readonly ManualResetEventSlim _drainReleased = new(initialState: false);
		private readonly object _stopLock = new();
		private Thread? _drainThread;

		/// <summary>Signalled as soon as the recorder asks the source to stop.</summary>
		public ManualResetEventSlim StopSignalled { get; } = new(initialState: false);

		/// <summary>PCM handed over after the stop, the way an already-filled buffer would be.</summary>
		public byte[]? BufferDeliveredWhileDraining { get; init; }

		/// <summary>Never raise <see cref="RecordingStopped"/>, standing in for a wedged driver.</summary>
		public bool NeverSignalsDrained { get; init; }

		public event EventHandler<WaveInEventArgs>? DataAvailable;

		public event EventHandler<StoppedEventArgs>? RecordingStopped;

		public void StartRecording()
		{
		}

		public void Deliver(byte[] pcm) => DataAvailable?.Invoke(this, new WaveInEventArgs(pcm, pcm.Length));

		public void StopRecording()
		{
			lock (_stopLock)
			{
				StopSignalled.Set();

				// Idempotent: AudioRecorder.Dispose stops again on its way out.
				if (_drainThread is not null)
					return;

				if (NeverSignalsDrained)
					return;

				_drainThread = new Thread(() =>
				{
					_drainReleased.Wait();
					if (BufferDeliveredWhileDraining is not null)
						Deliver(BufferDeliveredWhileDraining);

					RecordingStopped?.Invoke(this, new StoppedEventArgs());
				})
				{ IsBackground = true, Name = "fake-capture-drain" };
				_drainThread.Start();
			}
		}

		public void ReleaseDrain() => _drainReleased.Set();

		public void Dispose()
		{
			_drainReleased.Set();
			_drainThread?.Join(TimeSpan.FromSeconds(10));
			_drainReleased.Dispose();
			StopSignalled.Dispose();
		}
	}
}
