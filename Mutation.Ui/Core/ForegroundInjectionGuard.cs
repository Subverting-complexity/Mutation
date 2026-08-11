namespace Mutation.Ui.Core;

/// <summary>
/// Whether Windows is going to throw away a keystroke aimed at the window in front, worked
/// out from what a probe of that window's process managed to find out and nothing else.
/// <para>
/// User Interface Privilege Isolation stops an ordinary process sending input to a window
/// owned by one running at a higher integrity level. Microsoft's documentation for
/// <c>SendInput</c> is explicit that this failure is invisible from the calling side: the
/// events are accepted into the input stream, the return count is the full number submitted,
/// and <c>GetLastError</c> reports nothing. They are discarded further down.
/// </para>
/// <para>
/// So the count check that <see cref="Mutation.Ui.Services.HotkeyManager"/> does after the
/// fact cannot see the case this app was actually failing at — a transcript dictated with
/// Task Manager or an elevated console in front, announced as delivered, typed nowhere. It
/// does catch input genuinely refused, which is a different set of causes (issue #294).
/// </para>
/// <para>
/// The question has to be asked before sending, and the only reliable way to ask is to try
/// to open the foreground window's process for a query and see whether Windows says no. Kept
/// apart from the P/Invoke that does the asking so the rule can be exercised without an
/// elevated process to point it at.
/// </para>
/// </summary>
internal static class ForegroundInjectionGuard
{
	/// <summary>
	/// <c>ERROR_ACCESS_DENIED</c>. The one error code that means the foreground process sits
	/// above us, rather than that the question could not be asked.
	/// </summary>
	internal const int ErrorAccessDenied = 5;

	/// <summary>What trying to open the foreground window's process told us.</summary>
	internal enum ProbeResult
	{
		/// <summary>
		/// Nothing could be established: no window in front, no process id for it, or a
		/// failure that was not a refusal. Injection goes ahead.
		/// </summary>
		Unknown,

		/// <summary>
		/// The process opened, so it does not sit above us and our input will reach it.
		/// </summary>
		Opened,

		/// <summary>
		/// Windows refused the handle outright. The process is at a higher integrity level and
		/// anything we inject will be discarded without a word.
		/// </summary>
		Refused,
	}

	/// <summary>
	/// Reads the outcome of one probe. <paramref name="errorCode"/> is only consulted when
	/// <paramref name="opened"/> is false.
	/// <para>
	/// <c>PROCESS_QUERY_LIMITED_INFORMATION</c> is deliberately the weakest right that
	/// identifies a process — it is granted for a process at our own integrity level or below,
	/// and for processes belonging to other users. A flat refusal of it is therefore a strong
	/// signal rather than a routine one.
	/// </para>
	/// </summary>
	internal static ProbeResult Classify(bool hasForegroundWindow, uint processId, bool opened, int errorCode)
	{
		// No window in front — a locked screen, a moment between two apps — and nothing to
		// decide. Likewise a window whose thread we cannot resolve to a process.
		if (!hasForegroundWindow || processId == 0)
			return ProbeResult.Unknown;

		if (opened)
			return ProbeResult.Opened;

		return errorCode == ErrorAccessDenied ? ProbeResult.Refused : ProbeResult.Unknown;
	}

	/// <summary>
	/// True only for an outright refusal. Every other answer lets the injection proceed,
	/// deliberately: a false positive here would refuse to type into a perfectly ordinary
	/// window and tell the user their dictation failed when it would have worked, which is a
	/// worse outcome than the silent drop this exists to catch.
	/// </summary>
	internal static bool InputWillBeDiscarded(ProbeResult probe) => probe == ProbeResult.Refused;
}
