using System;
using Mutation.Ui;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="AudioDeviceManager.DisposeSuperseded{T}"/> — the rule that
/// releases the COM wrappers dropped on a hot-plug re-enumeration while keeping
/// the currently selected microphone alive. Exercised through a fake disposable
/// because real CoreAudio <c>MMDevice</c> instances need physical hardware.
/// </summary>
public class AudioDeviceDisposalTests
{
	private sealed class FakeDisposable : IDisposable
	{
		public int DisposeCount { get; private set; }
		public bool ThrowOnDispose { get; init; }

		public void Dispose()
		{
			DisposeCount++;
			if (ThrowOnDispose)
				throw new InvalidOperationException("boom");
		}
	}

	[Fact]
	public void Disposes_every_superseded_device_when_none_is_selected()
	{
		var a = new FakeDisposable();
		var b = new FakeDisposable();

		AudioDeviceManager.DisposeSuperseded(new[] { a, b }, selected: null);

		Assert.Equal(1, a.DisposeCount);
		Assert.Equal(1, b.DisposeCount);
	}

	[Fact]
	public void Skips_the_currently_selected_device()
	{
		var selected = new FakeDisposable();
		var other = new FakeDisposable();

		AudioDeviceManager.DisposeSuperseded(new[] { selected, other }, selected);

		Assert.Equal(0, selected.DisposeCount);
		Assert.Equal(1, other.DisposeCount);
	}

	[Fact]
	public void A_failing_dispose_does_not_strand_the_remaining_devices()
	{
		var bad = new FakeDisposable { ThrowOnDispose = true };
		var good = new FakeDisposable();

		AudioDeviceManager.DisposeSuperseded(new[] { bad, good }, selected: null);

		Assert.Equal(1, bad.DisposeCount);
		Assert.Equal(1, good.DisposeCount);
	}

	[Fact]
	public void Null_entries_are_ignored()
	{
		var real = new FakeDisposable();

		AudioDeviceManager.DisposeSuperseded(new FakeDisposable[] { null!, real }, selected: null);

		Assert.Equal(1, real.DisposeCount);
	}
}
