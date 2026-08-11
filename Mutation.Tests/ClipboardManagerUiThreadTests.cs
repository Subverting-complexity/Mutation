using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mutation.Ui.Services;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// The clipboard only answers calls made from the UI thread. These pin that every attempt in
/// the retry ladder is made there, not just the first one — which is what issue #352 was: the
/// waits inside <see cref="ClipboardRetry"/> handed attempts two to five to the thread pool,
/// where the clipboard refuses them for a reason no further attempt gets past, so these were
/// one-attempt operations wearing a retry ladder.
/// </summary>
public class ClipboardManagerUiThreadTests
{
	[Fact]
	public async Task TrySetTextAsync_MakesEveryRetryAttemptOnTheSameThreadAsTheFirst()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new RecordingClipboard(uiThread) { FailWrites = 2 };

		bool copied = await clipboard.TrySetTextAsync("transcript");

		Assert.True(copied);
		Assert.Equal(3, clipboard.WriteThreadIds.Count);
		Assert.All(clipboard.WriteThreadIds, id => Assert.Equal(uiThread.ThreadId, id));
	}

	/// <summary>
	/// The full ladder is still walked when the clipboard never lets go, and every rung of it is
	/// walked in the one place the clipboard will answer from.
	/// </summary>
	[Fact]
	public async Task TrySetTextAsync_WalksTheWholeLadderOnTheUiThread_WhenTheClipboardNeverLetsGo()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new RecordingClipboard(uiThread) { FailWrites = int.MaxValue };

		bool copied = await clipboard.TrySetTextAsync("transcript");

		Assert.False(copied);
		Assert.Equal(ClipboardRetry.DefaultAttempts, clipboard.WriteThreadIds.Count);
		Assert.All(clipboard.WriteThreadIds, id => Assert.Equal(uiThread.ThreadId, id));
	}

	/// <summary>
	/// Each attempt asks for the UI thread again rather than assuming the last wait left it
	/// there. Counting the hops is what separates this from a ladder that merely started in the
	/// right place.
	/// </summary>
	[Fact]
	public async Task TrySetTextAsync_AsksForTheUiThreadOncePerAttempt()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new RecordingClipboard(uiThread) { FailWrites = 2 };

		await clipboard.TrySetTextAsync("transcript");

		Assert.Equal(3, uiThread.DispatchCount);
	}

	[Fact]
	public async Task TrySetTextAsync_DoesNotTouchTheClipboard_ForBlankText()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new RecordingClipboard(uiThread);

		bool copied = await clipboard.TrySetTextAsync("   ");

		Assert.False(copied);
		Assert.Empty(clipboard.WriteThreadIds);
	}

	/// <summary>
	/// Without a dispatcher the call is made where the caller stands. Tests that care about what
	/// reached the clipboard rather than which thread put it there rely on this.
	/// </summary>
	[Fact]
	public async Task TrySetTextAsync_RunsInPlace_WhenThereIsNoDispatcher()
	{
		var clipboard = new RecordingClipboard(uiThread: null);

		bool copied = await clipboard.TrySetTextAsync("transcript");

		Assert.True(copied);
		Assert.Equal(new[] { Environment.CurrentManagedThreadId }, clipboard.WriteThreadIds);
	}

	/// <summary>
	/// A failure on the UI thread has to come back to the caller, or the retry above it has
	/// nothing to react to and a busy clipboard would read as a successful write.
	/// </summary>
	[Fact]
	public async Task RunAsync_FaultsTheCaller_WhenTheOperationThrowsOnTheUiThread()
	{
		using var uiThread = new SingleThreadUiDispatcher();

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => uiThread.RunAsync<object?>(() => throw new InvalidOperationException("clipboard busy")));
	}

	/// <summary>
	/// A <see cref="ClipboardManager"/> whose one clipboard call records the thread it was made
	/// on, and which can be told to fail the way a clipboard held open by another process does.
	/// </summary>
	private sealed class RecordingClipboard : ClipboardManager
	{
		private readonly List<int> _writeThreadIds = new();

		public RecordingClipboard(IUiThreadDispatcher? uiThread)
			: base(uiThread)
		{
		}

		public IReadOnlyList<int> WriteThreadIds => _writeThreadIds;

		/// <summary>
		/// How many of the next writes throw. <see cref="int.MaxValue"/> for a clipboard that
		/// never lets go.
		/// </summary>
		public int FailWrites { get; set; }

		public override void SetText(string text)
		{
			_writeThreadIds.Add(Environment.CurrentManagedThreadId);

			if (FailWrites <= 0)
				return;

			if (FailWrites != int.MaxValue)
				FailWrites--;

			// The real one is a COMException carrying CLIPBRD_E_CANT_OPEN when something else
			// has the clipboard, or RPC_E_WRONG_THREAD when the call was made from the wrong
			// place. All the retry above looks at is that the write threw.
			throw new InvalidOperationException("OpenClipboard Failed (0x800401D0)");
		}
	}
}
