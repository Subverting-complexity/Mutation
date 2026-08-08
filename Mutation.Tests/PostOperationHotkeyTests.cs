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
}
