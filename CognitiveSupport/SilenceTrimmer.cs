using System;
using System.Collections.Generic;

namespace CognitiveSupport;

/// <summary>
/// Streaming voice-activity trimmer that operates on fixed-size 16-bit PCM frames.
/// Feed frames in order via <see cref="ProcessFrame"/>; kept frames are handed back
/// through the supplied callback. Call <see cref="Flush"/> once the stream ends.
///
/// Rules:
///  - Leading/trailing silence is removed entirely except for a guard (pre-roll/hang-time).
///  - Silence between speech longer than the threshold is trimmed down to the threshold.
///  - Shorter inter-speech gaps are left untouched.
///  - The guard is always preserved around speech to avoid clipping word edges.
///
/// Not thread-safe; the caller must serialize access.
/// </summary>
public sealed class SilenceTrimmer
{
	// A removal is only ever recorded once per trimmed gap, so a multi-hour recording
	// produces a few thousand entries at most. The cap is a backstop against a
	// pathological stream (rapid speech/silence alternation for hours) growing this
	// list without bound on the audio capture path; past it, later gaps are still
	// trimmed, they are just not offered as split points.
	private const int MaxRecordedRemovals = 100_000;

	private readonly int _thresholdFrames;
	private readonly int _guardFrames;
	private readonly double _meanSquareThreshold;
	private readonly int _capFrames;
	private readonly double _frameSeconds;

	// First and last frames of the current silence gap. Bounded to _capFrames each so a
	// long pause does not grow memory without bound; we only ever keep frames adjacent to
	// speech (the acoustically important guard regions) plus up to the threshold budget.
	private readonly List<short[]> _head = new();
	private readonly List<short[]> _tail = new();
	private long _silenceCount;

	private bool _hasEmittedSpeech;

	// Frames handed to the emit callback so far. This is the processed timeline, which
	// is what a removal point has to be expressed in: it shortens as gaps are dropped,
	// so a position taken from it stays correct however much earlier audio was removed.
	private long _emittedFrameCount;

	private readonly List<SilenceRemovalPoint> _removedSilences = new();

	/// <summary>Number of speech (non-silent) frames seen so far.</summary>
	public long SpeechFrameCount { get; private set; }

	/// <summary>
	/// Every silence period removed so far, in output order, positioned on the
	/// processed timeline. Used to pick natural cut points when a recording has to be
	/// split into upload-sized chunks.
	/// </summary>
	public IReadOnlyList<SilenceRemovalPoint> RemovedSilences => _removedSilences;

	public SilenceTrimmer(int sampleRate, int frameSize, SilenceTrimmerOptions options)
	{
		if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
		if (frameSize <= 0) throw new ArgumentOutOfRangeException(nameof(frameSize));
		if (options is null) throw new ArgumentNullException(nameof(options));

		double frameMs = frameSize * 1000.0 / sampleRate;
		_frameSeconds = frameSize / (double)sampleRate;

		_thresholdFrames = Math.Max(0, (int)Math.Round(options.MinSilenceSeconds * 1000.0 / frameMs));
		_guardFrames = Math.Max(0, (int)Math.Round(options.GuardMilliseconds / frameMs));
		_capFrames = Math.Max(_thresholdFrames, 2 * _guardFrames);

		// Convert the dBFS floor to a mean-square threshold so detection avoids sqrt/log per frame.
		double linearRms = Math.Pow(10.0, options.SilenceThresholdDbFs / 20.0) * 32768.0;
		_meanSquareThreshold = linearRms * linearRms;
	}

	public void ProcessFrame(short[] frame, Action<short[]> emit)
	{
		if (frame is null) throw new ArgumentNullException(nameof(frame));
		if (emit is null) throw new ArgumentNullException(nameof(emit));

		if (IsSilent(frame))
		{
			BufferSilence(frame);
			return;
		}

		if (_silenceCount > 0)
			FlushGap(hasLeftSpeech: _hasEmittedSpeech, hasRightSpeech: true, emit);

		emit(frame);
		_emittedFrameCount++;
		_hasEmittedSpeech = true;
		SpeechFrameCount++;
	}

	/// <summary>Flush trailing silence. Call once after the final frame.</summary>
	public void Flush(Action<short[]> emit)
	{
		if (emit is null) throw new ArgumentNullException(nameof(emit));
		if (_silenceCount > 0)
			FlushGap(hasLeftSpeech: _hasEmittedSpeech, hasRightSpeech: false, emit);
	}

	private bool IsSilent(short[] frame)
	{
		double sumSquares = 0;
		for (int i = 0; i < frame.Length; i++)
		{
			double s = frame[i];
			sumSquares += s * s;
		}
		double meanSquare = sumSquares / frame.Length;
		return meanSquare <= _meanSquareThreshold;
	}

	private void BufferSilence(short[] frame)
	{
		_silenceCount++;
		if (_head.Count < _capFrames)
			_head.Add(frame);
		_tail.Add(frame);
		if (_tail.Count > _capFrames)
			_tail.RemoveAt(0);
	}

	private void FlushGap(bool hasLeftSpeech, bool hasRightSpeech, Action<short[]> emit)
	{
		long length = _silenceCount;
		int leftGuard = hasLeftSpeech ? _guardFrames : 0;
		int rightGuard = hasRightSpeech ? _guardFrames : 0;

		int keepStart;
		int keepEnd;

		if (!hasLeftSpeech && !hasRightSpeech)
		{
			// Entire stream was silence: keep nothing.
			keepStart = 0;
			keepEnd = 0;
		}
		else if (hasLeftSpeech && hasRightSpeech)
		{
			// Interior gap: trim down to the threshold, but never below the guard regions.
			int budget = (int)Math.Min(length, Math.Max(_thresholdFrames, leftGuard + rightGuard));
			keepEnd = Math.Min(rightGuard, budget);
			keepStart = budget - keepEnd; // any slack beyond the guards stays as hang-time after speech
		}
		else if (hasRightSpeech)
		{
			// Leading silence: keep only the pre-roll guard before the first speech.
			keepStart = 0;
			keepEnd = (int)Math.Min(length, rightGuard);
		}
		else
		{
			// Trailing silence: keep only the hang-time guard after the last speech.
			keepStart = (int)Math.Min(length, leftGuard);
			keepEnd = 0;
		}

		for (int i = 0; i < keepStart; i++)
		{
			emit(_head[i]);
			_emittedFrameCount++;
		}

		// Recorded between the two halves of the kept guard, which is exactly where the
		// dropped audio used to sit — so the position is the seam in the output, and any
		// later removal is measured against an output that has already lost this much.
		RecordRemoval(length - keepStart - keepEnd);

		for (int i = _tail.Count - keepEnd; i < _tail.Count; i++)
		{
			emit(_tail[i]);
			_emittedFrameCount++;
		}

		_head.Clear();
		_tail.Clear();
		_silenceCount = 0;
	}

	private void RecordRemoval(long droppedFrames)
	{
		if (droppedFrames <= 0 || _removedSilences.Count >= MaxRecordedRemovals)
			return;

		_removedSilences.Add(new SilenceRemovalPoint(
			TimeSpan.FromSeconds(_emittedFrameCount * _frameSeconds),
			TimeSpan.FromSeconds(droppedFrames * _frameSeconds)));
	}
}
