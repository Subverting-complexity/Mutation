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

	// ----- Paste chord (issue #170) -----

	[Fact]
	public void Parse_PasteChord_ParsesToControlV()
	{
		// The transcript-insertion paste path must use this exact chord so it
		// takes the SendInput route instead of the SendKeys.SendWait fallback.
		var hk = Hotkey.Parse("Ctrl+V");
		Assert.True(hk.Control);
		Assert.False(hk.Alt);
		Assert.False(hk.Shift);
		Assert.False(hk.Win);
		Assert.Equal(VirtualKey.V, hk.Key);
	}

	[Fact]
	public void Parse_CaretSyntax_IsNotSupported()
	{
		// "^v" is WinForms SendKeys syntax, not a parseable chord; sending it
		// forced every paste through the exception-driven SendWait fallback.
		Assert.Throws<NotSupportedException>(() => Hotkey.Parse("^v"));
	}

	// ----- ToString -----

	[Fact]
	public void ToString_NoKey_ReturnsNonePlaceholder()
	{
		var hk = new Hotkey();
		Assert.Equal("(none)", hk.ToString());
	}

	[Fact]
	public void ToString_ModifierOrderIsCtrlShiftAltWin()
	{
		// The one canonical spelling in the app. This used to be a third form —
		// "Shift+Control+Alt+Windows+A" — against the CTRL+SHIFT+ALT+WIN the registration
		// table and the hotkey editor both produced (issue #306).
		var hk = new Hotkey
		{
			Alt = true,
			Control = true,
			Shift = true,
			Win = true,
			Key = VirtualKey.A,
		};
		Assert.Equal("CTRL+SHIFT+ALT+WIN+A", hk.ToString());
	}

	[Fact]
	public void ToString_NumberKey_StripsNumberPrefix()
	{
		var hk = new Hotkey { Control = true, Key = VirtualKey.Number5 };
		Assert.Equal("CTRL+5", hk.ToString());
	}

	[Fact]
	public void ToString_NonNumberKey_KeepsName()
	{
		var hk = new Hotkey { Control = true, Key = VirtualKey.Delete };
		Assert.Equal("CTRL+DELETE", hk.ToString());
	}

	[Fact]
	public void ToString_AgreesWithTheRegistrationTables_normalized_form()
	{
		// Registration reports the chord it refused by this name. If the two ever drifted, the
		// message would name a shortcut the user cannot find on the Settings screen.
		var hk = new Hotkey { Control = true, Shift = true, Key = VirtualKey.F1 };

		Assert.Equal(HotkeyRegistrationTable.NormalizeHotkey(hk), hk.ToString());
	}

	// ----- Value equality -----

	[Fact]
	public void Two_spellings_of_one_chord_are_equal()
	{
		Assert.Equal(Hotkey.Parse("Ctrl-Shift-A"), Hotkey.Parse("SHIFT+CONTROL+a"));
	}

	[Fact]
	public void Equal_chords_share_a_hash_code()
	{
		// Without this a HashSet<Hotkey> would hold both spellings, and duplicate detection
		// would wave through a shortcut that is already taken.
		Assert.Equal(
			Hotkey.Parse("Ctrl-Shift-A").GetHashCode(),
			Hotkey.Parse("SHIFT+CONTROL+a").GetHashCode());
	}

	[Fact]
	public void A_different_key_is_a_different_chord()
	{
		Assert.NotEqual(Hotkey.Parse("Ctrl+A"), Hotkey.Parse("Ctrl+B"));
	}

	[Fact]
	public void A_missing_modifier_is_a_different_chord()
	{
		Assert.NotEqual(Hotkey.Parse("Ctrl+Shift+A"), Hotkey.Parse("Ctrl+A"));
	}

	[Fact]
	public void A_chord_is_never_equal_to_null_or_to_another_type()
	{
		var hk = Hotkey.Parse("Ctrl+A");

		Assert.False(hk.Equals(null));
		Assert.False(hk.Equals("CTRL+A"));
	}

	[Fact]
	public void A_clone_equals_the_chord_it_came_from()
	{
		var original = Hotkey.Parse("Ctrl+Alt+Delete");

		Assert.Equal(original, original.Clone());
	}

	// ----- TryParse -----

	[Fact]
	public void TryParse_returns_the_chord_for_text_that_parses()
	{
		Assert.True(Hotkey.TryParse("Ctrl+Alt+G", out var hk));
		Assert.Equal(Hotkey.Parse("Ctrl+Alt+G"), hk);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("CTRL+")]
	[InlineData("Ctrl+Shift")]
	[InlineData("^v")]
	[InlineData("Ctrl+NotAKey")]
	public void TryParse_answers_false_rather_than_throwing(string? text)
	{
		// Duplicate detection runs over whatever the user has typed so far, where half-finished
		// text is routine rather than an error.
		Assert.False(Hotkey.TryParse(text, out var hk));
		Assert.Null(hk);
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
