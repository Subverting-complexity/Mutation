using Mutation.Ui.Core;
using Mutation.Ui.Services;
using System;
using System.Collections.Generic;

namespace Mutation.Tests;

/// <summary>
/// How a wiggle move reaches the pointer (issue #377): announced as injected input, so a
/// magnifier watching the input stream sees it, and placed exactly, so it lands on the pixel the
/// wiggle asked for. Both, because neither alone does the job.
/// </summary>
public class ReportedCursorPositionTests
{
	private static readonly CursorPoint Target = new(400, 300);

	private sealed class FakeCursor : ICursorPosition
	{
		public CursorPoint Position;
		public bool WriteSucceeds = true;

		/// <summary>
		/// Positions this pretends the pointer ended up on, in order, whatever was asked for.
		/// Models an injected event arriving after the placement rather than before it.
		/// </summary>
		public Queue<CursorPoint> LandsOnInstead { get; } = new();

		public List<CursorPoint> Writes { get; } = new();

		public bool TryGet(out CursorPoint position)
		{
			position = Position;
			return true;
		}

		public bool TrySet(CursorPoint position)
		{
			Writes.Add(position);
			if (!WriteSucceeds)
				return false;

			Position = LandsOnInstead.Count > 0 ? LandsOnInstead.Dequeue() : position;
			return true;
		}
	}

	[Fact]
	public void TheMoveIsAnnouncedAndThenPlaced()
	{
		var cursor = new FakeCursor();
		var reported = new List<CursorPoint>();

		var subject = new ReportedCursorPosition(cursor, p => { reported.Add(p); return true; });

		Assert.True(subject.TrySet(Target));
		Assert.Equal(new[] { Target }, reported);
		Assert.Equal(Target, cursor.Position);
	}

	[Fact]
	public void ARefusedAnnouncementStillMovesThePointer()
	{
		// Injected movement is what makes the wiggle visible to a magnifier, not what makes it
		// happen. Windows refuses it outright when the app in front runs with more privilege than
		// Mutation, and the wiggle has to carry on regardless.
		var cursor = new FakeCursor();

		var subject = new ReportedCursorPosition(cursor, _ => false);

		Assert.True(subject.TrySet(Target));
		Assert.Equal(Target, cursor.Position);
	}

	[Fact]
	public void APlacementThatCannotBeMadeIsReportedAsFailure()
	{
		// The placement decides the answer, so that the wiggle's own checks — did my move take
		// effect, is the pointer still where I left it — keep meaning what they meant.
		var cursor = new FakeCursor { WriteSucceeds = false };

		var subject = new ReportedCursorPosition(cursor, _ => true);

		Assert.False(subject.TrySet(Target));
	}

	[Fact]
	public void AnInjectedMoveThatArrivesLastIsPutRight()
	{
		// Injected input is put into the input stream, not applied by the time the call returns,
		// so it can land after the placement. Left alone, the wiggle's next tick would find a
		// position it did not write, read it as the user taking the mouse, and stand down leaving
		// the pointer off its anchor.
		var cursor = new FakeCursor();
		cursor.LandsOnInstead.Enqueue(new CursorPoint(401, 300));

		var subject = new ReportedCursorPosition(cursor, _ => true);

		Assert.True(subject.TrySet(Target));
		Assert.Equal(Target, cursor.Position);
		Assert.Equal(2, cursor.Writes.Count);
	}

	[Fact]
	public void ReadingThePointerGoesStraightThrough()
	{
		var cursor = new FakeCursor { Position = Target };

		var subject = new ReportedCursorPosition(cursor, _ => true);

		Assert.True(subject.TryGet(out var position));
		Assert.Equal(Target, position);
	}

	[Fact]
	public void NullCursorIsRejected()
	{
		Assert.Throws<ArgumentNullException>(() => new ReportedCursorPosition(null!));
	}
}
