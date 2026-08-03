using Mutation.Ui.Core;

namespace Mutation.Tests;

// Issue #214: the accessible name was rebuilt from a cached, first-seen label, so a
// button whose state changed kept announcing its startup name. The name must come from
// the label for the button's *current* state.
public class HotkeyAccessibleTextTests
{
	[Fact]
	public void ComposeName_AppendsTheHotkey()
	{
		Assert.Equal("Record, SHIFT+ALT+U", HotkeyAccessibleText.ComposeName("Record", "SHIFT+ALT+U"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void ComposeName_OmitsAnAbsentHotkey(string? hotkey)
	{
		Assert.Equal("Record", HotkeyAccessibleText.ComposeName("Record", hotkey));
	}

	[Fact]
	public void ComposeTooltip_AppendsTheHotkeyInBrackets()
	{
		Assert.Equal("Stop (Hotkey: SHIFT+ALT+U)", HotkeyAccessibleText.ComposeTooltip("Stop", "SHIFT+ALT+U"));
		Assert.Equal("Stop", HotkeyAccessibleText.ComposeTooltip("Stop", null));
	}

	[Fact]
	public void ComposeHotkeyText_IsEmptyWithoutAHotkey()
	{
		Assert.Equal("Hotkey: ALT+Q", HotkeyAccessibleText.ComposeHotkeyText("ALT+Q"));
		Assert.Equal(string.Empty, HotkeyAccessibleText.ComposeHotkeyText(null));
	}
}
