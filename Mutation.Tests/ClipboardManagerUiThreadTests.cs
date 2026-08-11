using System;
using System.Threading.Tasks;
using Mutation.Ui.Services;
using Windows.Graphics.Imaging;
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
		var clipboard = new TestClipboard(uiThread) { FailWrites = 2 };

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
		var clipboard = new TestClipboard(uiThread) { FailWrites = int.MaxValue };

		bool copied = await clipboard.TrySetTextAsync("transcript");

		Assert.False(copied);
		Assert.Equal(ClipboardRetry.DefaultAttempts, clipboard.WriteThreadIds.Count);
		Assert.All(clipboard.WriteThreadIds, id => Assert.Equal(uiThread.ThreadId, id));
	}

	/// <summary>
	/// Each attempt asks for the UI thread again rather than assuming the last wait left it
	/// there. Counting the hops is what separates this from a ladder that merely started in the
	/// right place — one hop and four attempts would satisfy the thread-id checks above.
	/// </summary>
	[Fact]
	public async Task TrySetTextAsync_AsksForTheUiThreadOncePerAttempt()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new TestClipboard(uiThread) { FailWrites = 2 };

		await clipboard.TrySetTextAsync("transcript");

		Assert.Equal(3, uiThread.DispatchCount);
	}

	/// <summary>
	/// The snapshot taken and put back either side of a paste retries too. This is the half of
	/// the story's acceptance list that is not about dictation: a clipboard manager that holds
	/// on through the first attempt used to mean the user's own clipboard contents were not put
	/// back.
	/// </summary>
	[Fact]
	public async Task TryRestoreSnapshotAsync_RetriesOnTheUiThread_WhenTheClipboardIsBrieflyBusy()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new TestClipboard(uiThread) { FailWrites = 2 };
		var snapshot = new ClipboardSnapshot { Text = "what the user had before" };

		bool restored = await clipboard.TryRestoreSnapshotAsync(snapshot);

		Assert.True(restored);
		Assert.Same(snapshot, clipboard.LastRestoredSnapshot);
		Assert.Equal(3, clipboard.WriteThreadIds.Count);
		Assert.All(clipboard.WriteThreadIds, id => Assert.Equal(uiThread.ThreadId, id));
	}

	[Fact]
	public async Task TryRestoreSnapshotAsync_ReportsFailure_WhenTheClipboardNeverLetsGo()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new TestClipboard(uiThread) { FailWrites = int.MaxValue };

		bool restored = await clipboard.TryRestoreSnapshotAsync(
			new ClipboardSnapshot { Text = "what the user had before" });

		Assert.False(restored);
		Assert.Null(clipboard.LastRestoredSnapshot);
		Assert.Equal(ClipboardRetry.DefaultAttempts, clipboard.WriteThreadIds.Count);
	}

	/// <summary>
	/// The screenshot write retries too, and does it on the UI thread. This is the write that
	/// opens the race the two above were fixed for: it is the moment a clipboard manager or a
	/// screen reader notices something arrived and opens the clipboard to look. It was the one
	/// asynchronous write here with no retry and no hop, so a clipboard held open for a moment
	/// reached the user as an error dialog instead of a screenshot (issue #360).
	/// </summary>
	[Fact]
	public async Task TrySetImageAsync_RetriesOnTheUiThread_WhenTheClipboardIsBrieflyBusy()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new TestClipboard(uiThread) { FailWrites = 2 };
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);

		bool copied = await clipboard.TrySetImageAsync(image);

		Assert.True(copied);
		Assert.Same(image, clipboard.LastImage);
		Assert.Equal(3, clipboard.WriteThreadIds.Count);
		Assert.All(clipboard.WriteThreadIds, id => Assert.Equal(uiThread.ThreadId, id));
	}

	/// <summary>
	/// Each attempt asks for the UI thread again rather than trusting where the last wait left
	/// it. Counting the hops is what separates this from a ladder that merely started in the
	/// right place, which is what issue #352 turned out to be for the other writes.
	/// </summary>
	[Fact]
	public async Task TrySetImageAsync_AsksForTheUiThreadOncePerAttempt()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new TestClipboard(uiThread) { FailWrites = 2 };
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);

		await clipboard.TrySetImageAsync(image);

		Assert.Equal(3, uiThread.DispatchCount);
	}

	/// <summary>
	/// A clipboard that never lets go is reported, not thrown. The throw is what reached the
	/// hotkey handler as an error dialog; a bool is what every sibling on the class answers with,
	/// and it leaves the caller to decide what to say.
	/// </summary>
	[Fact]
	public async Task TrySetImageAsync_ReportsFailure_WhenTheClipboardNeverLetsGo()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new TestClipboard(uiThread) { FailWrites = int.MaxValue };
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);

		bool copied = await clipboard.TrySetImageAsync(image);

		Assert.False(copied);
		Assert.Null(clipboard.LastImage);
		Assert.Equal(ClipboardRetry.DefaultAttempts, clipboard.WriteThreadIds.Count);
		Assert.All(clipboard.WriteThreadIds, id => Assert.Equal(uiThread.ThreadId, id));
	}

	[Fact]
	public async Task TrySetTextAsync_DoesNotTouchTheClipboard_ForBlankText()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new TestClipboard(uiThread);

		bool copied = await clipboard.TrySetTextAsync("   ");

		Assert.False(copied);
		Assert.Empty(clipboard.WriteThreadIds);
	}

	/// <summary>
	/// A caller already on the UI thread is left there rather than queued behind itself. The
	/// hop count is what says so; the thread ids alone cannot tell the two apart.
	/// </summary>
	[Fact]
	public async Task TrySetTextAsync_RunsInPlace_WhenTheCallerIsAlreadyOnTheUiThread()
	{
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new TestClipboard(uiThread) { FailWrites = 2 };

		// Start the whole ladder on the dispatcher's own thread, which is what a real clipboard
		// call from a UI event handler looks like.
		bool copied = await uiThread.RunAsync(() => clipboard.TrySetTextAsync("transcript"));

		Assert.True(copied);
		Assert.Equal(3, clipboard.WriteThreadIds.Count);
		Assert.All(clipboard.WriteThreadIds, id => Assert.Equal(uiThread.ThreadId, id));

		// One hop to get the ladder started, and none for the attempts themselves: the first
		// runs in place, and the two after it are queued back because ClipboardRetry's wait
		// leaves them on the thread pool.
		Assert.Equal(3, uiThread.DispatchCount);
	}

	/// <summary>
	/// Without a dispatcher the call is made where the caller stands. Tests that care about what
	/// reached the clipboard rather than which thread put it there rely on this.
	/// </summary>
	[Fact]
	public async Task TrySetTextAsync_RunsInPlace_WhenThereIsNoDispatcher()
	{
		var clipboard = new TestClipboard(uiThread: null);
		int callerThreadId = Environment.CurrentManagedThreadId;

		bool copied = await clipboard.TrySetTextAsync("transcript");

		Assert.True(copied);
		Assert.Equal(new[] { callerThreadId }, clipboard.WriteThreadIds);
	}

	/// <summary>
	/// A failure on the UI thread has to come back to the caller, or the retry above it has
	/// nothing to react to and a busy clipboard would read as a successful write.
	/// <para>
	/// This pins the contract every <see cref="IUiThreadDispatcher"/> owes its caller, using the
	/// test double. It is not a test of <c>DispatcherQueueUiThread</c>, the one that ships:
	/// that needs a real WinUI dispatcher queue and a real UI thread, neither of which exists in
	/// this test assembly.
	/// </para>
	/// </summary>
	[Fact]
	public async Task ADispatcher_FaultsTheCaller_WhenTheOperationThrowsOnTheUiThread()
	{
		using var uiThread = new SingleThreadUiDispatcher();

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => uiThread.RunAsync<object?>(() => throw new InvalidOperationException("clipboard busy")));
	}

	private sealed class TestClipboard : RecordingClipboardManager
	{
		public TestClipboard(IUiThreadDispatcher? uiThread)
			: base(uiThread)
		{
		}
	}
}
