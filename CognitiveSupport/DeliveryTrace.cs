using System;
using System.Threading;

namespace CognitiveSupport;

/// <summary>
/// One timeline for the steps between a transcript landing and its confirmation being heard:
/// the paste, the shortcut sent afterwards, and the beep. Every step writes one short line, and
/// the app points all of them at <c>Mutation.Hotkey.log</c>, which already records the paste and
/// the shortcut with millisecond timestamps.
///
/// <para>
/// This exists because the late success beep and the late screen-reader shortcut (issue #411)
/// could not be placed. The log already proved the app *asked* for both on time (issue #386),
/// and nothing said what happened after that — whether <c>SendInput</c> came back promptly,
/// whether the beep's audio was actually pulled by the speaker, or whether the speaker
/// connection had failed and been reopened. Each of those points at a different cause, so each
/// one is now written down.
/// </para>
///
/// <para>
/// Static, and in this assembly rather than the UI one, because the beep lives here and the log
/// file lives there. The UI sets the sink once at startup. Until it does, or if it never does,
/// writing is a no-op — which is what the tests and anything else without a log get.
/// </para>
/// </summary>
public static class DeliveryTrace
{
	/// <summary>Put in front of every line so the timeline can be picked out of the rest of the log.</summary>
	public const string Prefix = "[delivery] ";

	private static Action<string>? s_sink;

	/// <summary>Where lines go from now on. Null stops writing.</summary>
	public static void SetSink(Action<string>? sink) => Volatile.Write(ref s_sink, sink);

	/// <summary>
	/// Writes one line. Never throws: this is called from the audio device's own thread and from
	/// the keystroke path, and a diagnostic must not be the thing that breaks either of them.
	/// </summary>
	public static void Write(string message)
	{
		var sink = Volatile.Read(ref s_sink);
		if (sink is null)
			return;

		try
		{
			sink(Prefix + message);
		}
		catch
		{
			// A log that cannot be written is not worth losing a beep or a keystroke over.
		}
	}
}
