namespace Mutation.Ui.Core;

/// <summary>
/// Whether Windows is going to throw away a keystroke aimed at the window in front, worked
/// out from what a probe of that window's process managed to find out and nothing else.
/// <para>
/// User Interface Privilege Isolation stops a process sending input to a window owned by one
/// running at a higher integrity level. Microsoft's documentation for <c>SendInput</c> is
/// explicit that this failure is invisible from the calling side: the events are accepted into
/// the input stream, the return count is the full number submitted, and <c>GetLastError</c>
/// reports nothing. They are discarded further down.
/// </para>
/// <para>
/// So the count check that <see cref="Mutation.Ui.Services.HotkeyManager"/> does after the
/// fact cannot see the case this app was actually failing at — a transcript dictated with
/// Task Manager or an elevated console in front, announced as delivered, typed nowhere. It
/// does catch input genuinely refused, which is a different set of causes (issue #294).
/// </para>
/// <para>
/// The question has to be asked before sending, and the answer is a comparison of two
/// integrity levels: ours and the foreground process's. Kept apart from the P/Invoke that
/// fetches them so the rule can be exercised without an elevated process to point it at.
/// </para>
/// <para>
/// Two access rights are asked for, and the difference between them is the signal.
/// <c>PROCESS_QUERY_LIMITED_INFORMATION</c> exists precisely so that an ordinary process
/// <em>can</em> identify a more privileged one — it is what lets an unelevated Task Manager
/// list elevated processes by name — so it is granted across an integrity boundary and a
/// refusal of it says nothing about privilege. <c>PROCESS_QUERY_INFORMATION</c> is not granted
/// across one, and it is the right <c>OpenProcessToken</c> needs. So:
/// </para>
/// <list type="bullet">
/// <item>Both granted — read the two integrity levels and compare them.</item>
/// <item>Limited granted, full refused — we can name the process but not read its token. That
/// is the integrity boundary's signature.</item>
/// <item>Limited refused — a DACL said no, which is a different question. Another user's
/// process in the same session refuses this while running at our own integrity level, where
/// UIPI would have let the input through. Treated as "cannot tell".</item>
/// </list>
/// <para>
/// That last case is why the refusal of the limited right is not read as a positive. Getting it
/// wrong would refuse to type into an ordinary window and report a failure that would not have
/// happened, which is worse than the silent drop this exists to catch.
/// </para>
/// </summary>
internal static class ForegroundInjectionGuard
{
	/// <summary>
	/// <c>ERROR_ACCESS_DENIED</c>. Windows saying no to a look at a process or its token,
	/// which it only does when that process sits above us.
	/// </summary>
	internal const int ErrorAccessDenied = 5;

	// The well-known integrity level RIDs, named so a test can talk about them. They are the
	// low twelve bits of the S-1-16-x SID that every token carries, and they are ordered — a
	// plain greater-than is the whole comparison.
	internal const uint UntrustedIntegrity = 0x0000;
	internal const uint LowIntegrity = 0x1000;
	internal const uint MediumIntegrity = 0x2000;
	internal const uint HighIntegrity = 0x3000;
	internal const uint SystemIntegrity = 0x4000;

	/// <summary>How one call in the probe went.</summary>
	internal enum ProbeStep
	{
		/// <summary>It worked.</summary>
		Succeeded,

		/// <summary>Windows refused it with <see cref="ErrorAccessDenied"/>.</summary>
		Refused,

		/// <summary>
		/// It failed for some other reason — the process exited between two calls, memory ran
		/// short. Says nothing about privilege either way.
		/// </summary>
		Failed,
	}

	/// <summary>What the probe as a whole established.</summary>
	internal enum ForegroundProbe
	{
		/// <summary>
		/// Nothing. No window in front, no process id for it, a call that failed for a reason
		/// unrelated to privilege, or a DACL refusing us a process we could otherwise have typed
		/// into. Injection goes ahead.
		/// </summary>
		Unknown,

		/// <summary>
		/// The process could be named but not read: limited query granted, full query refused.
		/// Nothing but an integrity boundary produces that pair, so it is conclusive on its own
		/// and no integrity level is needed to act on it.
		/// </summary>
		AboveUs,

		/// <summary>
		/// Both integrity levels were read, so the answer is a straight comparison.
		/// </summary>
		IntegrityKnown,
	}

	/// <summary>Reads one call's outcome. <paramref name="errorCode"/> matters only on failure.</summary>
	internal static ProbeStep StepFrom(bool succeeded, int errorCode)
	{
		if (succeeded)
			return ProbeStep.Succeeded;

		return errorCode == ErrorAccessDenied ? ProbeStep.Refused : ProbeStep.Failed;
	}

	/// <summary>
	/// What the probe's three calls together established.
	/// </summary>
	/// <param name="limitedOpen">
	/// Opening the process for <c>PROCESS_QUERY_LIMITED_INFORMATION</c>. Granted across an
	/// integrity boundary, so a refusal here is a DACL and tells us nothing we can act on.
	/// </param>
	/// <param name="fullOpen">
	/// Opening the same process for <c>PROCESS_QUERY_INFORMATION</c>. Not granted across an
	/// integrity boundary, so refused-here-but-granted-above is the signature we are looking for.
	/// </param>
	/// <param name="tokenRead">
	/// Opening the process's token, which needs the full right. Reached only when
	/// <paramref name="fullOpen"/> succeeded, so a failure is something unexpected rather than a
	/// privilege answer.
	/// </param>
	internal static ForegroundProbe Classify(
		bool hasForegroundWindow,
		uint processId,
		ProbeStep limitedOpen,
		ProbeStep fullOpen,
		ProbeStep tokenRead)
	{
		// No window in front — a locked screen, a moment between two apps — and nothing to
		// decide. Likewise a window whose thread we cannot resolve to a process.
		if (!hasForegroundWindow || processId == 0)
			return ForegroundProbe.Unknown;

		// Includes Refused. Another user's process in this session refuses even the limited
		// right while sitting at our own integrity level, where our input would have landed.
		if (limitedOpen != ProbeStep.Succeeded)
			return ForegroundProbe.Unknown;

		if (fullOpen == ProbeStep.Refused)
			return ForegroundProbe.AboveUs;

		if (fullOpen != ProbeStep.Succeeded || tokenRead != ProbeStep.Succeeded)
			return ForegroundProbe.Unknown;

		return ForegroundProbe.IntegrityKnown;
	}

	/// <summary>
	/// Whether our input is going to be discarded. Equal integrity is fine — UIPI blocks
	/// sending up, not sending across — so the comparison is strictly greater than.
	/// <para>
	/// Deliberately one-sided everywhere else: anything that could not be established lets the
	/// injection proceed. A false positive here would refuse to type into a perfectly ordinary
	/// window and tell the user their dictation failed when it would have worked, which is a
	/// worse outcome than the silent drop this exists to catch. It is also why the user guide
	/// still says this catch is not a guarantee.
	/// </para>
	/// </summary>
	internal static bool InputWillBeDiscarded(
		ForegroundProbe probe, uint foregroundIntegrity, uint ownIntegrity) =>
		probe switch
		{
			ForegroundProbe.AboveUs => true,
			ForegroundProbe.IntegrityKnown => foregroundIntegrity > ownIntegrity,
			_ => false,
		};
}
