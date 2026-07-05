using System;
using System.Collections.Generic;
using CognitiveSupport;

namespace Mutation.Tests;

public class PcmFrameSplitterTests
{
	private const int FrameSamples = 4; // small frame keeps the test data readable
	private const int FrameBytes = FrameSamples * 2;

	// Little-endian 16-bit encoding of the given samples.
	private static byte[] Bytes(params short[] samples)
	{
		var b = new byte[samples.Length * 2];
		Buffer.BlockCopy(samples, 0, b, 0, b.Length);
		return b;
	}

	private static List<short[]> Collect(Action<Action<short[]>> drive)
	{
		var frames = new List<short[]>();
		drive(frames.Add);
		return frames;
	}

	[Fact]
	public void Append_WholeFrames_EmitsEachFrameWithCorrectSamples()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		var input = Bytes(1, 2, 3, 4, 5, 6, 7, 8); // exactly two frames

		var frames = Collect(emit => splitter.Append(input, input.Length, emit));

		Assert.Equal(2, frames.Count);
		Assert.Equal(new short[] { 1, 2, 3, 4 }, frames[0]);
		Assert.Equal(new short[] { 5, 6, 7, 8 }, frames[1]);
	}

	[Fact]
	public void Append_PartialFrame_EmitsNothingUntilCompleted()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		var first = Bytes(1, 2);   // half a frame
		var second = Bytes(3, 4);  // completes it

		var frames = new List<short[]>();
		splitter.Append(first, first.Length, frames.Add);
		Assert.Empty(frames);

		splitter.Append(second, second.Length, frames.Add);
		Assert.Single(frames);
		Assert.Equal(new short[] { 1, 2, 3, 4 }, frames[0]);
	}

	[Fact]
	public void Append_CarriesRemainderAcrossCalls()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		// 1.5 frames, then another 1.5 frames -> 3 whole frames total.
		var a = Bytes(1, 2, 3, 4, 5, 6);
		var b = Bytes(7, 8, 9, 10, 11, 12);

		var frames = new List<short[]>();
		splitter.Append(a, a.Length, frames.Add);
		Assert.Single(frames); // {1,2,3,4}; {5,6} carried

		splitter.Append(b, b.Length, frames.Add);
		Assert.Equal(3, frames.Count);
		Assert.Equal(new short[] { 1, 2, 3, 4 }, frames[0]);
		Assert.Equal(new short[] { 5, 6, 7, 8 }, frames[1]);
		Assert.Equal(new short[] { 9, 10, 11, 12 }, frames[2]);
	}

	[Fact]
	public void Append_ByteAtATime_ReconstructsFramesCorrectly()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		var input = Bytes(100, 200, 300, 400, 500, 600, 700, 800);

		var frames = new List<short[]>();
		var single = new byte[1];
		foreach (var value in input)
		{
			single[0] = value;
			splitter.Append(single, 1, frames.Add);
		}

		Assert.Equal(2, frames.Count);
		Assert.Equal(new short[] { 100, 200, 300, 400 }, frames[0]);
		Assert.Equal(new short[] { 500, 600, 700, 800 }, frames[1]);
	}

	[Fact]
	public void Append_HonorsCountOverBufferLength()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		// Buffer is larger than the valid data; only the first frame's worth is real.
		var buffer = Bytes(1, 2, 3, 4, 999, 999, 999, 999);

		var frames = Collect(emit => splitter.Append(buffer, FrameBytes, emit));

		Assert.Single(frames);
		Assert.Equal(new short[] { 1, 2, 3, 4 }, frames[0]);
	}

	[Fact]
	public void Flush_PadWithSilence_EmitsZeroPaddedRemainder()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		var input = Bytes(1, 2); // half a frame remains buffered

		var frames = new List<short[]>();
		splitter.Append(input, input.Length, frames.Add);
		splitter.Flush(frames.Add, padWithSilence: true);

		Assert.Single(frames);
		Assert.Equal(new short[] { 1, 2, 0, 0 }, frames[0]);
	}

	[Fact]
	public void Flush_WithoutPadding_DropsRemainder()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		var input = Bytes(1, 2);

		var frames = new List<short[]>();
		splitter.Append(input, input.Length, frames.Add);
		splitter.Flush(frames.Add, padWithSilence: false);

		Assert.Empty(frames);
	}

	[Fact]
	public void Flush_WithNoRemainder_EmitsNothing()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		var input = Bytes(1, 2, 3, 4);

		var frames = new List<short[]>();
		splitter.Append(input, input.Length, frames.Add);
		splitter.Flush(frames.Add, padWithSilence: true);

		Assert.Single(frames); // only the whole frame; nothing extra from flush
	}

	[Fact]
	public void Flush_ClearsRemainderSoSecondFlushIsNoOp()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		splitter.Append(Bytes(1, 2), FrameBytes / 2, (_) => { });

		var frames = new List<short[]>();
		splitter.Flush(frames.Add, padWithSilence: true);
		splitter.Flush(frames.Add, padWithSilence: true);

		Assert.Single(frames);
	}

	[Fact]
	public void EmittedFrames_AreIndependentArrays()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		var input = Bytes(1, 2, 3, 4, 5, 6, 7, 8);

		var frames = Collect(emit => splitter.Append(input, input.Length, emit));

		// Mutating one emitted frame must not affect the other (required because
		// SilenceTrimmer retains frame references).
		frames[0][0] = -1;
		Assert.Equal(new short[] { 5, 6, 7, 8 }, frames[1]);
	}

	[Fact]
	public void Append_DecodesLittleEndian()
	{
		var splitter = new PcmFrameSplitter(1);
		// 0x34, 0x12 little-endian -> 0x1234.
		var input = new byte[] { 0x34, 0x12 };

		var frames = Collect(emit => splitter.Append(input, input.Length, emit));

		Assert.Single(frames);
		Assert.Equal(0x1234, frames[0][0]);
	}

	[Fact]
	public void Constructor_RejectsNonPositiveFrameSize()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new PcmFrameSplitter(0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PcmFrameSplitter(-1));
	}

	[Fact]
	public void Append_RejectsNullArguments()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		Assert.Throws<ArgumentNullException>(() => splitter.Append(null!, 0, _ => { }));
		Assert.Throws<ArgumentNullException>(() => splitter.Append(new byte[4], 4, null!));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(5)] // larger than the 4-byte source
	public void Append_RejectsCountOutOfRange(int count)
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => splitter.Append(new byte[4], count, _ => { }));
	}

	[Fact]
	public void Flush_RejectsNullEmit()
	{
		var splitter = new PcmFrameSplitter(FrameSamples);
		Assert.Throws<ArgumentNullException>(() => splitter.Flush(null!, padWithSilence: true));
	}
}
