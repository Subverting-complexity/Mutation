using Mutation.Ui.Core;
using System;
using System.Collections.Generic;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// The rules that keep the mouse pointer where the user left it while a screen capture opens
/// and closes its full-screen overlay (issue #371). Two of them carry the whole feature: put
/// the pointer back when something else has moved it, and leave it completely alone when
/// nothing has.
/// </summary>
public class CursorAnchorTests
{
	/// <summary>
	/// A pointer that only exists in the test. Records every write, so a test can assert not
	/// just where the pointer ended up but whether it was written to at all — which is the
	/// difference between "put back" and "left alone".
	/// </summary>
	private sealed class FakeCursor : ICursorPosition
	{
		public CursorPoint Position;
		public bool CanRead = true;
		public bool WriteSucceeds = true;
		public List<CursorPoint> Writes { get; } = new();
		public int Reads { get; private set; }

		public FakeCursor(int x = 0, int y = 0) => Position = new CursorPoint(x, y);

		public bool TryGet(out CursorPoint position)
		{
			Reads++;
			if (!CanRead)
			{
				position = default;
				return false;
			}

			position = Position;
			return true;
		}

		public bool TrySet(CursorPoint position)
		{
			Writes.Add(position);
			if (!WriteSucceeds)
				return false;

			Position = position;
			return true;
		}
	}

	[Fact]
	public void NullCursor_IsRejectedAtConstruction()
	{
		Assert.Throws<ArgumentNullException>(() => new CursorAnchor(null!));
	}

	[Fact]
	public void PointerMovedWhileTheOverlayOpened_IsPutBackWhereItWas()
	{
		var cursor = new FakeCursor(400, 300);
		var anchor = new CursorAnchor(cursor);

		anchor.Capture();
		// Something following focus drags the pointer to the middle of the new window.
		cursor.Position = new CursorPoint(960, 540);

		Assert.True(anchor.Restore());
		Assert.Equal(new CursorPoint(400, 300), cursor.Position);
	}

	[Fact]
	public void PointerThatNeverMoved_IsNotWrittenToAtAll()
	{
		var cursor = new FakeCursor(400, 300);
		var anchor = new CursorAnchor(cursor);

		anchor.Capture();

		// Writing the same coordinates back would still raise a mouse-move message, which is
		// enough to cancel a hover or disturb a drag. Doing nothing has to mean doing nothing.
		Assert.False(anchor.Restore());
		Assert.Empty(cursor.Writes);
	}

	[Fact]
	public void RestoreWithoutACapture_DoesNothing()
	{
		var cursor = new FakeCursor(400, 300);
		var anchor = new CursorAnchor(cursor);

		Assert.False(anchor.HasAnchor);
		Assert.False(anchor.Restore());
		Assert.Empty(cursor.Writes);
	}

	[Fact]
	public void AnchorSurvivesRestore_SoASecondJumpIsAlsoUndone()
	{
		// The capture restores twice on the way in — inline, then on the next dispatcher turn,
		// because whatever follows focus reacts after the activation call has returned.
		var cursor = new FakeCursor(400, 300);
		var anchor = new CursorAnchor(cursor);

		anchor.Capture();
		cursor.Position = new CursorPoint(10, 10);
		Assert.True(anchor.Restore());

		cursor.Position = new CursorPoint(20, 20);
		Assert.True(anchor.Restore());

		Assert.Equal(new CursorPoint(400, 300), cursor.Position);
		Assert.Equal(2, cursor.Writes.Count);
	}

	[Fact]
	public void SecondCaptureReplacesTheFirst()
	{
		// One anchor serves the whole capture: opened at the hotkey press, re-read when the
		// selection ends so the pointer goes back to where the drag was released, not to where
		// it started.
		var cursor = new FakeCursor(400, 300);
		var anchor = new CursorAnchor(cursor);

		anchor.Capture();
		cursor.Position = new CursorPoint(800, 600);
		anchor.Capture();

		cursor.Position = new CursorPoint(0, 0);
		Assert.True(anchor.Restore());
		Assert.Equal(new CursorPoint(800, 600), cursor.Position);
	}

	[Fact]
	public void PositionThatCannotBeRead_LeavesNothingToRestoreTo()
	{
		var cursor = new FakeCursor(400, 300) { CanRead = false };
		var anchor = new CursorAnchor(cursor);

		Assert.False(anchor.Capture());
		Assert.False(anchor.HasAnchor);

		cursor.CanRead = true;
		cursor.Position = new CursorPoint(10, 10);

		// A capture that read nothing must not send the pointer to a stale or default place.
		Assert.False(anchor.Restore());
		Assert.Empty(cursor.Writes);
	}

	[Fact]
	public void PositionThatCannotBeReadAtRestoreTime_IsNotOverwrittenBlindly()
	{
		var cursor = new FakeCursor(400, 300);
		var anchor = new CursorAnchor(cursor);

		anchor.Capture();
		cursor.CanRead = false;

		// Not knowing where the pointer is, is not a reason to move it.
		Assert.False(anchor.Restore());
		Assert.Empty(cursor.Writes);
	}

	[Fact]
	public void RefusedWrite_IsReportedAsNoMove()
	{
		// The caller redraws the crosshair only when the pointer really moved, so a refused
		// write has to read as "nothing happened".
		var cursor = new FakeCursor(400, 300) { WriteSucceeds = false };
		var anchor = new CursorAnchor(cursor);

		anchor.Capture();
		cursor.Position = new CursorPoint(10, 10);

		Assert.False(anchor.Restore());
		Assert.Single(cursor.Writes);
		Assert.Equal(new CursorPoint(10, 10), cursor.Position);
	}

	[Fact]
	public void ClearedAnchor_IsNoLongerDefended()
	{
		// Once the capture is over the pointer belongs to the user, and a later stray restore
		// must not haul it back.
		var cursor = new FakeCursor(400, 300);
		var anchor = new CursorAnchor(cursor);

		anchor.Capture();
		anchor.Clear();
		Assert.False(anchor.HasAnchor);

		cursor.Position = new CursorPoint(10, 10);
		Assert.False(anchor.Restore());
		Assert.Empty(cursor.Writes);
		Assert.Equal(new CursorPoint(10, 10), cursor.Position);
	}

	[Fact]
	public void CapturedAnchorIsReadable()
	{
		var cursor = new FakeCursor(-1920, 40);
		var anchor = new CursorAnchor(cursor);

		Assert.True(anchor.Capture());
		Assert.True(anchor.HasAnchor);
		// Negative coordinates are ordinary: a monitor left of the primary one has them.
		Assert.Equal(new CursorPoint(-1920, 40), anchor.Anchor);
	}
}
