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
		// Both sides are written canonically by the app, so this only matters for a settings
		// file someone typed by hand.
		var (state, _) = RouterBindingStatus.For(
			"CTRL+ALT+1", "CTRL+SHIFT+M", [Route(" ctrl+alt+1 ", "ctrl+shift+m")]);

		Assert.Equal(HotkeyBindingState.Bound, state);
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
