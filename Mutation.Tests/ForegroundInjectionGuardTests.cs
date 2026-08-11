using Mutation.Ui.Core;

namespace Mutation.Tests;

// The rule that decides whether an elevated window in front is going to swallow the
// transcript, without needing an elevated window to point it at (issue #294). The P/Invoke
// that feeds it lives in ForegroundIntegrityProbe and cannot be covered here.
public class ForegroundInjectionGuardTests
{
	private const ForegroundInjectionGuard.ProbeStep Ok = ForegroundInjectionGuard.ProbeStep.Succeeded;
	private const ForegroundInjectionGuard.ProbeStep Refused = ForegroundInjectionGuard.ProbeStep.Refused;
	private const ForegroundInjectionGuard.ProbeStep Failed = ForegroundInjectionGuard.ProbeStep.Failed;

	private static bool WillDiscard(
		ForegroundInjectionGuard.ForegroundProbe probe, uint theirs = 0, uint ours = 0) =>
		ForegroundInjectionGuard.InputWillBeDiscarded(probe, theirs, ours);

	// ----- Reading one call's outcome -----

	[Fact]
	public void StepFrom_TellsARefusalApartFromAnyOtherFailure()
	{
		// The distinction the whole rule turns on, so it is worth its own test.
		Assert.Equal(Ok, ForegroundInjectionGuard.StepFrom(true, 0));
		Assert.Equal(Refused, ForegroundInjectionGuard.StepFrom(false, ForegroundInjectionGuard.ErrorAccessDenied));
		// ERROR_INVALID_PARAMETER — the process exited between two calls.
		Assert.Equal(Failed, ForegroundInjectionGuard.StepFrom(false, 87));
		Assert.Equal(Failed, ForegroundInjectionGuard.StepFrom(false, 0));
	}

	[Fact]
	public void StepFrom_ASuccessIgnoresWhateverErrorCodeWasLyingAround()
	{
		// GetLastError is not cleared by a successful call, so a stale code can arrive alongside
		// a handle. The handle wins.
		Assert.Equal(Ok, ForegroundInjectionGuard.StepFrom(true, ForegroundInjectionGuard.ErrorAccessDenied));
	}

	// ----- The signature of an integrity boundary -----

	[Fact]
	public void NamedButNotReadable_IsTheOneThingThatMeansAboveUs()
	{
		// PROCESS_QUERY_LIMITED_INFORMATION is granted across an integrity boundary on purpose —
		// it is what lets an unelevated Task Manager list elevated processes by name — while
		// PROCESS_QUERY_INFORMATION is not. Granted the first and refused the second is the pair
		// nothing else produces.
		var probe = ForegroundInjectionGuard.Classify(
			hasForegroundWindow: true, processId: 4321, limitedOpen: Ok, fullOpen: Refused, tokenRead: Failed);

		Assert.Equal(ForegroundInjectionGuard.ForegroundProbe.AboveUs, probe);
		Assert.True(WillDiscard(probe));
	}

	[Fact]
	public void ADaclRefusingEvenTheLimitedRight_TellsUsNothingWeCanActOn()
	{
		// Another user's process in the same session refuses this while running at our own
		// integrity level, where UIPI would have let the input straight through. Reading it as
		// "above us" would refuse to type into an ordinary window and report a failure that would
		// not have happened — the one outcome worse than the silent drop this exists to catch.
		var probe = ForegroundInjectionGuard.Classify(
			hasForegroundWindow: true, processId: 4321, limitedOpen: Refused, fullOpen: Failed, tokenRead: Failed);

		Assert.Equal(ForegroundInjectionGuard.ForegroundProbe.Unknown, probe);
		Assert.False(WillDiscard(probe));
	}

	// ----- Comparing the two integrity levels -----

	[Fact]
	public void AHigherIntegrityForegroundProcess_WillDiscardOurInput()
	{
		var probe = ForegroundInjectionGuard.Classify(true, 4321, Ok, Ok, Ok);

		Assert.Equal(ForegroundInjectionGuard.ForegroundProbe.IntegrityKnown, probe);
		Assert.True(WillDiscard(
			probe,
			theirs: ForegroundInjectionGuard.HighIntegrity,
			ours: ForegroundInjectionGuard.MediumIntegrity));
	}

	[Fact]
	public void EqualIntegrity_IsFine()
	{
		// UIPI blocks sending up, not sending across. The ordinary case: two medium-integrity
		// apps, which is nearly every window the user dictates into.
		Assert.False(WillDiscard(
			ForegroundInjectionGuard.ForegroundProbe.IntegrityKnown,
			theirs: ForegroundInjectionGuard.MediumIntegrity,
			ours: ForegroundInjectionGuard.MediumIntegrity));
	}

	[Fact]
	public void ALowerIntegrityForegroundProcess_IsFine()
	{
		// A sandboxed browser tab process, say. Sending down is allowed.
		Assert.False(WillDiscard(
			ForegroundInjectionGuard.ForegroundProbe.IntegrityKnown,
			theirs: ForegroundInjectionGuard.LowIntegrity,
			ours: ForegroundInjectionGuard.MediumIntegrity));
	}

	[Fact]
	public void AnElevatedMutationTypingIntoAnElevatedApp_IsFine()
	{
		// Both above medium, and equal, so nothing is blocked.
		Assert.False(WillDiscard(
			ForegroundInjectionGuard.ForegroundProbe.IntegrityKnown,
			theirs: ForegroundInjectionGuard.HighIntegrity,
			ours: ForegroundInjectionGuard.HighIntegrity));
	}

	[Fact]
	public void ASystemIntegrityWindow_WillDiscardOurInput()
	{
		Assert.True(WillDiscard(
			ForegroundInjectionGuard.ForegroundProbe.IntegrityKnown,
			theirs: ForegroundInjectionGuard.SystemIntegrity,
			ours: ForegroundInjectionGuard.HighIntegrity));
	}

	// ----- Everything that is not an answer -----

	[Fact]
	public void NoWindowInFront_IsNotAnAnswerEitherWay()
	{
		// A locked screen or a moment between two apps. Nothing to decide, and refusing to
		// deliver on the strength of it would invent a failure.
		var probe = ForegroundInjectionGuard.Classify(false, 0, Failed, Failed, Failed);

		Assert.Equal(ForegroundInjectionGuard.ForegroundProbe.Unknown, probe);
		Assert.False(WillDiscard(probe));
	}

	[Fact]
	public void AWindowWhoseProcessCannotBeResolved_IsNotAnAnswerEither()
	{
		Assert.Equal(
			ForegroundInjectionGuard.ForegroundProbe.Unknown,
			ForegroundInjectionGuard.Classify(true, 0, Ok, Ok, Ok));
	}

	[Fact]
	public void TheFullOpenFailingForSomeOtherReason_LetsDeliveryProceed()
	{
		// The process exited between the two calls, say. Says nothing about privilege.
		var probe = ForegroundInjectionGuard.Classify(true, 4321, Ok, Failed, Failed);

		Assert.Equal(ForegroundInjectionGuard.ForegroundProbe.Unknown, probe);
		Assert.False(WillDiscard(probe));
	}

	[Fact]
	public void TheTokenFailingForSomeOtherReason_LetsDeliveryProceed()
	{
		var probe = ForegroundInjectionGuard.Classify(true, 4321, Ok, Ok, Failed);

		Assert.Equal(ForegroundInjectionGuard.ForegroundProbe.Unknown, probe);
		Assert.False(WillDiscard(probe));
	}

	[Fact]
	public void ARefusedTokenAfterAGrantedFullOpen_IsStillAboveUs()
	{
		// Not expected — the full right is what OpenProcessToken needs — but if Windows refuses
		// the token anyway, a refusal is a refusal.
		var probe = ForegroundInjectionGuard.Classify(true, 4321, Ok, Ok, Refused);

		Assert.Equal(ForegroundInjectionGuard.ForegroundProbe.Unknown, probe);
	}

	[Fact]
	public void AboveUs_NeedsNoIntegrityLevelsToActOn()
	{
		// The refusal is conclusive by itself, so the zeros the probe hands over in that case
		// must not be read as "equal integrity, carry on".
		Assert.True(WillDiscard(ForegroundInjectionGuard.ForegroundProbe.AboveUs, theirs: 0, ours: 0));
	}
}
