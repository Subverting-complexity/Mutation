using Mutation.Ui.Core;
using System;

namespace Mutation.Ui.Services;

/// <summary>
/// Moves the pointer so that a magnifier watching the input stream sees it move, and still lands
/// on the exact pixel asked for.
///
/// <para>
/// It does both because neither alone is enough. Injected mouse input is what an assistive tool
/// notices, but its absolute form is normalised onto a 0 to 65535 grid across the virtual desktop
/// and rounds, so it cannot be trusted to place the pointer to the pixel. Placing the cursor
/// directly is exact but invisible to a low-level hook. So each move is reported as input first
/// and then placed exactly, and it is the placement that decides the answer — which keeps every
/// promise made further up: that a wiggle ends on the pixel it started from, and that reading the
/// pointer back tells you whether your own move took effect.
/// </para>
///
/// <para>
/// Wrapped around the plain cursor rather than replacing it, and used only by the wiggle. Putting
/// the pointer back where the user left it is about placing it exactly and has no need to be
/// noticed by anyone, so it stays on the plain path.
/// </para>
///
/// <para>
/// A refused injection is not a failure. The pointer still moves; only the magnifier misses it,
/// which is no worse than not having tried.
/// </para>
/// </summary>
internal sealed class ReportedCursorPosition : ICursorPosition
{
	private readonly ICursorPosition _cursor;
	private readonly Func<CursorPoint, bool> _report;

	/// <param name="cursor">Reads the pointer, and places it exactly.</param>
	/// <param name="report">Announces a move as injected input. Injected by tests; the real one
	/// goes to <see cref="MouseInput"/>.</param>
	public ReportedCursorPosition(ICursorPosition cursor, Func<CursorPoint, bool>? report = null)
	{
		_cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
		_report = report ?? (position => MouseInput.TryReportMoveTo(position.X, position.Y));
	}

	public bool TryGet(out CursorPoint position) => _cursor.TryGet(out position);

	public bool TrySet(CursorPoint position)
	{
		_report(position);

		if (!_cursor.TrySet(position))
			return false;

		// Injected input is put into the input stream, not applied by the time the call returns,
		// so it can land after the placement above rather than before it. The two aim at the same
		// pixel, so agreeing is the ordinary case — but if the pointer is not where it was just
		// put, the injected event got there second and this puts it right. Left uncorrected, the
		// wiggle's next tick would find a position it did not write, read that as the user taking
		// the mouse, and stand down leaving the pointer off its anchor.
		if (_cursor.TryGet(out var landed) && landed != position)
			return _cursor.TrySet(position);

		return true;
	}
}
