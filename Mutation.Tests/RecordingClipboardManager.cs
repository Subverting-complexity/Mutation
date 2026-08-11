using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mutation.Ui.Services;
using Windows.Graphics.Imaging;

namespace Mutation.Tests;

/// <summary>
/// A <see cref="ClipboardManager"/> whose writes are recorded rather than made, and which can be
/// told to fail the way a clipboard held open by another process does.
/// <para>
/// Shared, because the threading tests and the OCR tests both need exactly this and had grown
/// their own copies of it. The OCR ones subclass it to add the image the clipboard is pretending
/// to hold.
/// </para>
/// </summary>
internal class RecordingClipboardManager : ClipboardManager
{
	private readonly List<int> _writeThreadIds = new();

	/// <param name="uiThread">
	/// Null for tests that only care what reached the clipboard, so the write happens where the
	/// test stands. Supply one to check which thread the write was made on.
	/// </param>
	protected RecordingClipboardManager(IUiThreadDispatcher? uiThread)
		: base(uiThread)
	{
	}

	/// <summary>The last text written, or null when nothing was written.</summary>
	public string? LastText { get; private set; }

	/// <summary>The last snapshot restored, or null when none was.</summary>
	public ClipboardSnapshot? LastRestoredSnapshot { get; private set; }

	/// <summary>The last image written, or null when none was.</summary>
	public SoftwareBitmap? LastImage { get; private set; }

	public int SetTextCalls { get; private set; }

	/// <summary>
	/// The thread each write was made on, in order. A plain list is enough: every path that
	/// reaches these doubles makes its writes one at a time, and the task completion between a
	/// write and the assertion that reads it supplies the memory barrier.
	/// </summary>
	public IReadOnlyList<int> WriteThreadIds => _writeThreadIds;

	/// <summary>
	/// How many of the next writes throw. <see cref="int.MaxValue"/> for a clipboard that never
	/// lets go.
	/// </summary>
	public int FailWrites { get; set; }

	/// <summary>
	/// The operation name and last failure of every ladder that ran out of attempts, in order.
	/// Watched here rather than written to the real error log, which other tests in this
	/// assembly read and rotate.
	/// </summary>
	public IReadOnlyList<(string OperationName, Exception? LastFailure)> GaveUp => _gaveUp;

	private readonly List<(string OperationName, Exception? LastFailure)> _gaveUp = new();

	protected override void OnClipboardGaveUp(string operationName, Exception? lastFailure)
	{
		_gaveUp.Add((operationName, lastFailure));
	}

	public override void SetText(string text)
	{
		SetTextCalls++;
		if (RecordWriteAndDecideToFail())
			return;

		LastText = text;
	}

	protected override Task RestoreSnapshotAsync(ClipboardSnapshot snapshot)
	{
		if (RecordWriteAndDecideToFail())
			return Task.CompletedTask;

		LastRestoredSnapshot = snapshot;
		return Task.CompletedTask;
	}

	/// <summary>
	/// The screenshot write. Recorded rather than made for the same reason as the two above: the
	/// real one encodes a PNG and hands it to the Windows clipboard, neither of which a test can
	/// check the retrying of.
	/// </summary>
	protected override Task SetImageAsync(SoftwareBitmap bitmap)
	{
		if (RecordWriteAndDecideToFail())
			return Task.CompletedTask;

		LastImage = bitmap;
		return Task.CompletedTask;
	}

	/// <summary>
	/// Records the thread this write was made on and throws when the double is still in its
	/// failing streak. Never returns true — the throw is the "failed" answer — but it is written
	/// as a guard so each caller reads as "record, maybe fail, then do the work".
	/// </summary>
	private bool RecordWriteAndDecideToFail()
	{
		_writeThreadIds.Add(Environment.CurrentManagedThreadId);

		if (FailWrites <= 0)
			return false;

		if (FailWrites != int.MaxValue)
			FailWrites--;

		// The real one is a COMException carrying CLIPBRD_E_CANT_OPEN when something else has
		// the clipboard, or RPC_E_WRONG_THREAD when the call was made from the wrong place. All
		// the retry above looks at is that the write threw.
		throw new InvalidOperationException("OpenClipboard Failed (0x800401D0)");
	}
}
