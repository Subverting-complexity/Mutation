using Mutation.Ui.Core;

namespace Mutation.Tests;

// The rule that decides whether an elevated window in front is going to swallow the
// transcript, without needing an elevated window to point it at (issue #294). The P/Invoke
// that feeds it lives in ForegroundIntegrityProbe and cannot be covered here.
public class ForegroundInjectionGuardTests
{
	[Fact]
	public void AnOutrightRefusal_MeansTheInputWillBeDiscarded()
	{
		// The whole point. Windows would not even let us identify the process, which it only
		// does when the process is above us — and above us means UIPI drops our keystrokes.
		var probe = ForegroundInjectionGuard.Classify(
			hasForegroundWindow: true,
			processId: 4321,
			opened: false,
			errorCode: ForegroundInjectionGuard.ErrorAccessDenied);

		Assert.Equal(ForegroundInjectionGuard.ProbeResult.Refused, probe);
		Assert.True(ForegroundInjectionGuard.InputWillBeDiscarded(probe));
	}

	[Fact]
	public void AProcessWeCanOpen_IsFineToTypeInto()
	{
		var probe = ForegroundInjectionGuard.Classify(
			hasForegroundWindow: true, processId: 4321, opened: true, errorCode: 0);

		Assert.Equal(ForegroundInjectionGuard.ProbeResult.Opened, probe);
		Assert.False(ForegroundInjectionGuard.InputWillBeDiscarded(probe));
	}

	[Fact]
	public void NoWindowInFront_IsNotAnAnswerEitherWay()
	{
		// A locked screen or a moment between two apps. Nothing to decide, and refusing to
		// deliver on the strength of it would invent a failure.
		var probe = ForegroundInjectionGuard.Classify(
			hasForegroundWindow: false, processId: 0, opened: false, errorCode: 0);

		Assert.Equal(ForegroundInjectionGuard.ProbeResult.Unknown, probe);
		Assert.False(ForegroundInjectionGuard.InputWillBeDiscarded(probe));
	}

	[Fact]
	public void AWindowWhoseProcessCannotBeResolved_IsNotAnAnswerEither()
	{
		var probe = ForegroundInjectionGuard.Classify(
			hasForegroundWindow: true, processId: 0, opened: false, errorCode: 0);

		Assert.Equal(ForegroundInjectionGuard.ProbeResult.Unknown, probe);
	}

	[Theory]
	// ERROR_INVALID_PARAMETER — the process exited between the two calls.
	[InlineData(87)]
	// ERROR_NOT_ENOUGH_MEMORY.
	[InlineData(8)]
	// No error reported at all, which should not happen after a failed OpenProcess.
	[InlineData(0)]
	public void AFailureThatIsNotARefusal_LetsDeliveryProceed(int errorCode)
	{
		// Deliberately one-sided. A false positive here would refuse to type into an ordinary
		// window and tell the user their dictation failed when it would have worked — worse
		// than the silent drop this check exists to catch.
		var probe = ForegroundInjectionGuard.Classify(
			hasForegroundWindow: true, processId: 4321, opened: false, errorCode: errorCode);

		Assert.Equal(ForegroundInjectionGuard.ProbeResult.Unknown, probe);
		Assert.False(ForegroundInjectionGuard.InputWillBeDiscarded(probe));
	}

	[Fact]
	public void ASuccessfulOpenIgnoresWhateverErrorCodeWasLyingAround()
	{
		// GetLastError is not cleared by a successful call, so a stale code can arrive
		// alongside a handle. The handle wins.
		var probe = ForegroundInjectionGuard.Classify(
			hasForegroundWindow: true,
			processId: 4321,
			opened: true,
			errorCode: ForegroundInjectionGuard.ErrorAccessDenied);

		Assert.Equal(ForegroundInjectionGuard.ProbeResult.Opened, probe);
		Assert.False(ForegroundInjectionGuard.InputWillBeDiscarded(probe));
	}
}
