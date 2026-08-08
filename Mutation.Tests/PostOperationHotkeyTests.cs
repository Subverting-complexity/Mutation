using Mutation.Ui.Core;

namespace Mutation.Tests;

/// <summary>
/// When the shortcut configured to run after an operation is sent.
/// </summary>
public class PostOperationHotkeyTests
{
	[Fact]
	public void OcrSendsTheShortcutWhenItSucceeded()
	{
		Assert.Equal(PostOperationHotkey.SuccessDelayMs, PostOperationHotkey.OcrDelay(true));
	}

	[Fact]
	public void OcrAlsoSendsTheShortcutWhenItFailed()
	{
		// Not zero and not skipped: the OCR shortcuts are commonly routed to a screen-reader
		// command that reads the result area, and a user working by ear needs to hear the
		// error just as much as the text.
		Assert.Equal(PostOperationHotkey.FailureDelayMs, PostOperationHotkey.OcrDelay(false));
		Assert.True(PostOperationHotkey.OcrDelay(false) > 0);
	}

	[Fact]
	public void TheOtherApplicationIsGivenLongerAfterTextArrives()
	{
		// The delay is there for the window receiving the text, so the success case — the
		// one with text settling in it — has to be the longer of the two.
		Assert.True(PostOperationHotkey.SuccessDelayMs > PostOperationHotkey.FailureDelayMs);
	}

	[Fact]
	public void ARunThatWasRefusedSendsNothing()
	{
		// Pressing an OCR shortcut while a capture is already on screen changes nothing: the
		// OCR box still holds the last run's answer, and what is in front of the user is a
		// full-screen capture overlay. The shortcut would be typed into it (issue #342).
		Assert.False(PostOperationHotkey.ShouldSendAfterOcr(OcrRunOutcome.Refused));
	}

	[Fact]
	public void ARunThatReachedAnAnswerStillSendsIt()
	{
		// Including a failed one. Narrowing the send to successes would be a worse bug than
		// the one being fixed — see the test above on the failure delay.
		Assert.True(PostOperationHotkey.ShouldSendAfterOcr(OcrRunOutcome.Answered));
	}

	[Fact]
	public void ThePasteComesBeforeTheConfiguredShortcut()
	{
		// The whole reason the two travel as one sequence. The shortcut people put in "Send
		// hotkey after OCR" is usually a screen-reader command aimed at the result, and a read
		// that happens before the paste reads whatever was there before (issue #355).
		Assert.Equal("Ctrl+V, ^{DEL}", PostOperationHotkey.AfterOcr(true, "^{DEL}"));
	}

	[Fact]
	public void PastingIsAllTheresIsToSendWhenNoShortcutIsConfigured()
	{
		Assert.Equal("Ctrl+V", PostOperationHotkey.AfterOcr(true, null));
		Assert.Equal("Ctrl+V", PostOperationHotkey.AfterOcr(true, "   "));
	}

	[Fact]
	public void NotPastingLeavesTheConfiguredShortcutExactlyAsItWas()
	{
		Assert.Equal("^{DEL}", PostOperationHotkey.AfterOcr(false, "^{DEL}"));
		Assert.Equal("Ctrl+C, Ctrl+V", PostOperationHotkey.AfterOcr(false, "Ctrl+C, Ctrl+V"));
	}

	[Fact]
	public void NothingToPasteAndNothingConfiguredSendsNothing()
	{
		// Null rather than an empty string: SendHotkeyAfterDelay reads null as "no send", and
		// an empty chord would otherwise reach the parser.
		Assert.Null(PostOperationHotkey.AfterOcr(false, null));
		Assert.Null(PostOperationHotkey.AfterOcr(false, ""));
	}

	[Fact]
	public void ASurroundingSpaceInTheConfiguredValueDoesNotBecomeAnEmptyChord()
	{
		// The sequence is split on commas, so a stray space either side of the join would be
		// harmless — but a value stored as " " must not turn into "Ctrl+V, " and read as two.
		Assert.Equal("Ctrl+V, ^{DEL}", PostOperationHotkey.AfterOcr(true, "  ^{DEL}  "));
	}

	[Fact]
	public void ThePasteChordIsSpelledTheWayTheParserReadsIt()
	{
		// "^v" would throw in Hotkey.Parse and drop the whole sequence — the configured
		// shortcut included — to the WinForms fallback, which cannot confirm delivery (#232).
		Assert.Equal("Ctrl+V", PostOperationHotkey.PasteChord);
	}
}
