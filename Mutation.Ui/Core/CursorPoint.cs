namespace Mutation.Ui.Core;

/// <summary>
/// A mouse pointer position in virtual-screen pixels — the coordinate space Windows itself
/// reports and accepts for the pointer, spanning every monitor.
/// <para>
/// Deliberately not in device-independent pixels. A value of this type is only ever produced
/// by reading the live pointer position and only ever consumed by writing that same position
/// back, so no DPI scale is applied on the way through and none can be got wrong. The overlay
/// converts to its own coordinates separately, for drawing.
/// </para>
/// </summary>
public readonly record struct CursorPoint(int X, int Y);
