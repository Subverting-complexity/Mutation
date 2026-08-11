using Mutation.Ui.Core;

namespace Mutation.Tests;

// Telling a router row that is live from one the user has only typed. Settings pages edit a
// copy and hand it over on save, so those are two different things and the row had no way to
// say which it was (issue #343).
public class RouterBindingStatusTests
{
	private static RegisteredRouterRoute Route(string from, string to, bool success = true, string? error = null)
		=> new(from, to, success, error);

	[Fact]
	public void ARowMatchingARegisteredRoute_IsLive()
	{
		var (state, message) = RouterBindingStatus.For(
			"CTRL+ALT+1", "CTRL+SHIFT+M", [Route("CTRL+ALT+1", "CTRL+SHIFT+M")]);

		Assert.Equal(HotkeyBindingState.Bound, state);
		Assert.Null(message);
	}

	[Fact]
	public void ARowMatchingARouteThatFailedToRegister_CarriesTheReason()
	{
		var (state, message) = RouterBindingStatus.For(
			"CTRL+ALT+1",
			"CTRL+SHIFT+M",
			[Route("CTRL+ALT+1", "CTRL+SHIFT+M", success: false, error: "The shortcut is already registered by another application.")]);

		Assert.Equal(HotkeyBindingState.Failed, state);
		Assert.Equal("The shortcut is already registered by another application.", message);
	}

	[Fact]
	public void ARowTheAppHasNeverSeen_IsWaitingForASave()
	{
		var (state, message) = RouterBindingStatus.For(
			"CTRL+ALT+2", "CTRL+SHIFT+M", [Route("CTRL+ALT+1", "CTRL+SHIFT+M")]);

		Assert.Equal(HotkeyBindingState.NotYetApplied, state);
		Assert.Null(message);
	}

	[Fact]
	public void ChangingOnlyWhatIsSent_StopsTheRowCallingItselfLive()
	{
		// The chord being listened for is still registered, but what it sends is now out of
		// date. A row that matched on the "From" side alone would tell the user their edit had
		// taken effect when it had not.
		var (state, _) = RouterBindingStatus.For(
			"CTRL+ALT+1", "CTRL+SHIFT+N", [Route("CTRL+ALT+1", "CTRL+SHIFT+M")]);

		Assert.Equal(HotkeyBindingState.NotYetApplied, state);
	}

	[Fact]
	public void NoWayToAsk_MeansNothingIsClaimedEitherWay()
	{
		// A null list is "we cannot find out" — which is the state the Settings page was
		// permanently in, while reporting every row as not bound.
		var (state, message) = RouterBindingStatus.For("CTRL+ALT+1", "CTRL+SHIFT+M", null);

		Assert.Equal(HotkeyBindingState.Unknown, state);
		Assert.Null(message);
	}

	[Fact]
	public void AnEmptyListIsARealAnswer_NotAnAbsentOne()
	{
		// The app holds no routes at all, so a valid row is genuinely waiting for a save.
		var (state, _) = RouterBindingStatus.For("CTRL+ALT+1", "CTRL+SHIFT+M", []);

		Assert.Equal(HotkeyBindingState.NotYetApplied, state);
	}

	[Theory]
	[InlineData(null, "CTRL+SHIFT+M")]
	[InlineData("CTRL+ALT+1", null)]
	[InlineData("", "CTRL+SHIFT+M")]
	[InlineData("   ", "   ")]
	public void HalfAMapping_SaysNothing(string? from, string? to)
	{
		// Not a route and cannot be one. The row's own "Enter a hotkey." already says so, and a
		// second line about not being active would repeat it.
		var (state, message) = RouterBindingStatus.For(from, to, [Route("CTRL+ALT+1", "CTRL+SHIFT+M")]);

		Assert.Equal(HotkeyBindingState.Unknown, state);
		Assert.Null(message);
	}

	[Fact]
	public void SpellingIsForgivenSoAHandEditedFileStillMatches()
	{
		var (state, _) = RouterBindingStatus.For(
			"CTRL+ALT+1", "CTRL+SHIFT+M", [Route(" ctrl+alt+1 ", "ctrl+shift+m")]);

		Assert.Equal(HotkeyBindingState.Bound, state);
	}

	[Fact]
	public void TheShippedDefaultMappingIsRecognisedAsLive()
	{
		// The bug this test exists for. SettingsManager seeds a brand-new settings file with a
		// router mapping written CONTROL+SHIFT+ALT+8, and the row's side of the comparison has
		// been through Canonicalize, which spells it CTRL+SHIFT+ALT+8. Comparing the two as text
		// meant that on a fresh install the only router row on the page reported a working
		// mapping as "not active yet" — the same class of false statement issue #343 was filed
		// about.
		var (state, _) = RouterBindingStatus.For(
			"CTRL+SHIFT+ALT+8",
			"CTRL+SHIFT+ALT+9",
			[Route("CONTROL+SHIFT+ALT+8", "CONTROL+SHIFT+ALT+9")]);

		Assert.Equal(HotkeyBindingState.Bound, state);
	}

	[Theory]
	// The order the modifiers were typed in, which files written before the canonicalisation
	// work still carry.
	[InlineData("SHIFT+CTRL+A")]
	// The long spelling of the same modifier.
	[InlineData("CONTROL+SHIFT+A")]
	[InlineData("control+shift+a")]
	public void AChordSpelledAnotherWayIsStillTheSameChord(string onDisk)
	{
		// A shortcut has one identity and many spellings. HotkeyConflictFinder learned this for
		// duplicate detection in issue #306; comparing spellings gets it wrong here too.
		var (state, _) = RouterBindingStatus.For(
			"CTRL+SHIFT+A", "CTRL+V", [Route(onDisk, "CTRL+V")]);

		Assert.Equal(HotkeyBindingState.Bound, state);
	}

	[Fact]
	public void ADifferentChordIsStillADifferentChord()
	{
		// Forgiving the spelling must not go so far as forgiving the shortcut.
		var (state, _) = RouterBindingStatus.For(
			"CTRL+SHIFT+A", "CTRL+V", [Route("CTRL+SHIFT+B", "CTRL+V")]);

		Assert.Equal(HotkeyBindingState.NotYetApplied, state);
	}

	[Fact]
	public void TheFirstMatchingRouteAnswers()
	{
		// Two identical routes cannot both be registered — the second is refused as a duplicate
		// — so the row reports the one that took.
		var (state, _) = RouterBindingStatus.For(
			"CTRL+ALT+1",
			"CTRL+SHIFT+M",
			[
				Route("CTRL+ALT+1", "CTRL+SHIFT+M"),
				Route("CTRL+ALT+1", "CTRL+SHIFT+M", success: false, error: "The shortcut is already registered."),
			]);

		Assert.Equal(HotkeyBindingState.Bound, state);
	}
}
