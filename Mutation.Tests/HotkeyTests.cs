using Mutation.Ui.Services;
using Windows.System;

namespace Mutation.Tests;

public class HotkeyTests
{
	// ----- Parse: separators -----

	[Theory]
	[InlineData("Ctrl+C")]
	[InlineData("Ctrl-C")]
	[InlineData("Ctrl C")]
	[InlineData("Ctrl,C")]
	[InlineData("Ctrl/C")]
	[InlineData("Ctrl\\C")]
	[InlineData("Ctrl|C")]
	[InlineData("Ctrl;C")]
	[InlineData("Ctrl:C")]
	public void Parse_AcceptsAllTokenSeparators(string text)
	{
		var hk = Hotkey.Parse(text);
		Assert.True(hk.Control);
		Assert.Equal(VirtualKey.C, hk.Key);
	}

	// ----- Parse: modifier aliases -----

	[Theory]
	[InlineData("CTRL+A")]
	[InlineData("Control+A")]
	[InlineData("control+A")]
	public void Parse_ControlAliases_SetControlFlag(string text)
	{
		var hk = Hotkey.Parse(text);
		Assert.True(hk.Control);
	}

	[Theory]
	[InlineData("Shift+A")]
	[InlineData("SHFT+A")]
	[InlineData("shft+A")]
	public void Parse_ShiftAliases_SetShiftFlag(string text)
	{
		var hk = Hotkey.Parse(text);
		Assert.True(hk.Shift);
	}

	[Theory]
	[InlineData("Win+A")]
	[InlineData("Windows+A")]
	[InlineData("Start+A")]
	public void Parse_WinAliases_SetWinFlag(string text)
	{
		var hk = Hotkey.Parse(text);
		Assert.True(hk.Win);
	}

	[Fact]
	public void Parse_AltModifier_SetsAltFlag()
	{
		var hk = Hotkey.Parse("Alt+A");
		Assert.True(hk.Alt);
		Assert.False(hk.Control);
		Assert.False(hk.Shift);
		Assert.False(hk.Win);
	}

	[Fact]
	public void Parse_AllModifiers_SetAll()
	{
		var hk = Hotkey.Parse("Ctrl+Shift+Alt+Win+A");
		Assert.True(hk.Control);
		Assert.True(hk.Shift);
		Assert.True(hk.Alt);
		Assert.True(hk.Win);
		Assert.Equal(VirtualKey.A, hk.Key);
	}

	// ----- Parse: number key handling -----

	[Fact]
	public void Parse_NumberPrefix_MapsToNumberKey()
	{
		var hk = Hotkey.Parse("Ctrl+Number5");
		Assert.Equal(VirtualKey.Number5, hk.Key);
	}

	[Fact]
	public void Parse_BareDigit_MapsToNumberKey()
	{
		// Numeric tokens prefer the "NumberN" alias so that "Ctrl+5" binds to
		// VirtualKey.Number5 rather than VirtualKey.XButton1 (the int-5 enum member).
		var hk = Hotkey.Parse("Ctrl+5");
		Assert.Equal(VirtualKey.Number5, hk.Key);
	}

	[Theory]
	[InlineData("0", VirtualKey.Number0)]
	[InlineData("9", VirtualKey.Number9)]
	public void Parse_AllBareDigits_MapToNumberKeys(string digit, VirtualKey expected)
	{
		var hk = Hotkey.Parse("Ctrl+" + digit);
		Assert.Equal(expected, hk.Key);
	}

	[Fact]
	public void Parse_LowercaseTokens_StillRecognized()
	{
		var hk = Hotkey.Parse("ctrl+shift+a");
		Assert.True(hk.Control);
		Assert.True(hk.Shift);
		Assert.Equal(VirtualKey.A, hk.Key);
	}

	// ----- Parse: error paths -----

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Parse_NullOrWhitespace_Throws(string? input)
	{
		Assert.Throws<ArgumentException>(() => Hotkey.Parse(input!));
	}

	[Fact]
	public void Parse_OnlyModifiers_Throws()
	{
		Assert.Throws<ArgumentException>(() => Hotkey.Parse("Ctrl+Shift"));
	}

	[Fact]
	public void Parse_UnknownToken_Throws()
	{
		Assert.Throws<NotSupportedException>(() => Hotkey.Parse("Ctrl+Bogus"));
	}

	// ----- ToString -----

	[Fact]
	public void ToString_NoKey_ReturnsNonePlaceholder()
	{
		var hk = new Hotkey();
		Assert.Equal("(none)", hk.ToString());
	}

	[Fact]
	public void ToString_ModifierOrderIsShiftControlAltWindows()
	{
		var hk = new Hotkey
		{
			Alt = true,
			Control = true,
			Shift = true,
			Win = true,
			Key = VirtualKey.A,
		};
		Assert.Equal("Shift+Control+Alt+Windows+A", hk.ToString());
	}

	[Fact]
	public void ToString_NumberKey_StripsNumberPrefix()
	{
		var hk = new Hotkey { Control = true, Key = VirtualKey.Number5 };
		Assert.Equal("Control+5", hk.ToString());
	}

	[Fact]
	public void ToString_NonNumberKey_KeepsName()
	{
		var hk = new Hotkey { Control = true, Key = VirtualKey.Delete };
		Assert.Equal("Control+Delete", hk.ToString());
	}

	// ----- Round-trip -----

	[Theory]
	[InlineData("Control+C")]
	[InlineData("Shift+Control+A")]
	[InlineData("Control+Alt+Delete")]
	[InlineData("Control+5")]
	public void RoundTrip_ParseToStringParseAgain_Equivalent(string canonical)
	{
		var first = Hotkey.Parse(canonical);
		var second = Hotkey.Parse(first.ToString());

		Assert.Equal(first.Alt, second.Alt);
		Assert.Equal(first.Control, second.Control);
		Assert.Equal(first.Shift, second.Shift);
		Assert.Equal(first.Win, second.Win);
		Assert.Equal(first.Key, second.Key);
	}

	// ----- Clone -----

	[Fact]
	public void Clone_ProducesIndependentCopy()
	{
		var original = new Hotkey
		{
			Control = true,
			Shift = true,
			Key = VirtualKey.A,
		};

		var copy = original.Clone();
		copy.Control = false;
		copy.Key = VirtualKey.B;

		Assert.True(original.Control);
		Assert.Equal(VirtualKey.A, original.Key);
		Assert.False(copy.Control);
		Assert.Equal(VirtualKey.B, copy.Key);
	}
}
