using Mutation.Ui.Services;
using Windows.System;

namespace Mutation.Tests;

/// <summary>
/// Reading a shortcut written in SendKeys notation back into a chord.
/// <para>
/// Issue #325: the two "send this shortcut afterwards" settings accept SendKeys notation,
/// and a settings file saved years ago holds "^{DEL}". Nothing could parse that, so it
/// only ever reached the WinForms fallback, which cannot say whether the keystrokes
/// arrived. These tests pin the readings so those settings keep going down the path that
/// can report a failure.
/// </para>
/// </summary>
public class SendKeysChordTests
{
	[Fact]
	public void CtrlDelete_TheShippedSetting_IsRead()
	{
		Assert.True(SendKeysChord.TryParse("^{DEL}", out var hotkey));

		Assert.True(hotkey.Control);
		Assert.False(hotkey.Shift);
		Assert.False(hotkey.Alt);
		Assert.False(hotkey.Win);
		Assert.Equal(VirtualKey.Delete, hotkey.Key);
	}

	[Theory]
	[InlineData("^{DELETE}")]
	[InlineData("^{del}")]
	[InlineData("  ^{DEL}  ")]
	public void CtrlDelete_IsReadHoweverItIsSpelled(string text)
	{
		Assert.True(SendKeysChord.TryParse(text, out var hotkey));
		Assert.True(hotkey.Control);
		Assert.Equal(VirtualKey.Delete, hotkey.Key);
	}

	[Fact]
	public void EveryModifierIsRecognised()
	{
		Assert.True(SendKeysChord.TryParse("^+%{HOME}", out var hotkey));

		Assert.True(hotkey.Control);
		Assert.True(hotkey.Shift);
		Assert.True(hotkey.Alt);
		Assert.Equal(VirtualKey.Home, hotkey.Key);
	}

	[Theory]
	[InlineData("%{F4}", VirtualKey.F4)]
	[InlineData("^{F12}", VirtualKey.F12)]
	[InlineData("^{PGDN}", VirtualKey.PageDown)]
	[InlineData("^{PGUP}", VirtualKey.PageUp)]
	[InlineData("+{TAB}", VirtualKey.Tab)]
	[InlineData("^{ENTER}", VirtualKey.Enter)]
	[InlineData("^{INS}", VirtualKey.Insert)]
	[InlineData("^{BACKSPACE}", VirtualKey.Back)]
	[InlineData("^{ESC}", VirtualKey.Escape)]
	[InlineData("^{SPACE}", VirtualKey.Space)]
	[InlineData("^{END}", VirtualKey.End)]
	[InlineData("^{UP}", VirtualKey.Up)]
	public void NamedKeysAreRead(string text, VirtualKey expected)
	{
		Assert.True(SendKeysChord.TryParse(text, out var hotkey));
		Assert.Equal(expected, hotkey.Key);
	}

	[Theory]
	[InlineData("^c", VirtualKey.C)]
	[InlineData("^C", VirtualKey.C)]
	[InlineData("^v", VirtualKey.V)]
	[InlineData("%1", VirtualKey.Number1)]
	public void SingleCharacterKeysAreRead(string text, VirtualKey expected)
	{
		Assert.True(SendKeysChord.TryParse(text, out var hotkey));
		Assert.Equal(expected, hotkey.Key);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	public void NothingToSendIsRefused(string? text)
	{
		Assert.False(SendKeysChord.TryParse(text, out _));
	}

	[Theory]
	[InlineData("c")]                // no modifier and no braces: Hotkey.Parse's job
	[InlineData("Ctrl+Delete")]      // the ordinary spelling: Hotkey.Parse's job
	[InlineData("^{DEL}{DEL}")]      // a sequence, not a chord
	[InlineData("^{DEL 2}")]         // a repeat count
	[InlineData("^(ab)")]            // a group
	[InlineData("^{}")]              // an empty name
	[InlineData("^{NOSUCHKEY}")]
	[InlineData("^{F25}")]           // beyond F24
	[InlineData("^{F0}")]
	[InlineData("^ab")]              // more than one key
	[InlineData("^;")]               // layout-dependent punctuation
	[InlineData("^{+}")]             // a braced literal, also layout-dependent
	[InlineData("^")]                // a modifier with nothing to press
	public void AnythingItCannotExpressIsRefusedRatherThanGuessed(string text)
	{
		// Refusing leaves the WinForms fallback to try. Guessing would send keystrokes the
		// user did not ask for, into whichever window they are working in.
		Assert.False(SendKeysChord.TryParse(text, out _));
	}

	[Fact]
	public void WinKeyIsNeverProduced()
	{
		// SendKeys has no notation for it, so no reading here should ever set it.
		Assert.True(SendKeysChord.TryParse("^+%{DEL}", out var hotkey));
		Assert.False(hotkey.Win);
	}

	[Theory]
	[InlineData("Ctrl+Delete")]
	[InlineData("Alt+F4")]
	[InlineData("Ctrl+Shift+Tab")]
	[InlineData("Ctrl+C")]
	[InlineData("Alt+PageDown")]
	[InlineData("Shift+Home")]
	[InlineData("Ctrl+Insert")]
	[InlineData("Ctrl+Alt+End")]
	public void WhatSendKeysMapperEmitsCanBeReadBack(string chord)
	{
		// The two are inverses for the plain chord case. If the mapper gains a spelling this
		// cannot read, the shortcut silently drops back to the fallback path.
		var mapped = SendKeysMapper.Map(chord);

		Assert.True(SendKeysChord.TryParse(mapped, out var readBack), $"'{chord}' mapped to '{mapped}' and could not be read back.");
		Assert.Equal(Hotkey.Parse(chord), readBack);
	}

	[Theory]
	[InlineData("Ctrl+PgDn", VirtualKey.PageDown)]
	[InlineData("Ctrl+Bksp", VirtualKey.Back)]
	[InlineData("Ctrl+Esc", VirtualKey.Escape)]
	public void TheMappersOwnKeyAliasesAlsoReadBack(string chord, VirtualKey expected)
	{
		// SendKeysMapper accepts short names Hotkey.Parse does not, so these round-trip
		// through the mapped form rather than through a second Hotkey.Parse.
		var mapped = SendKeysMapper.Map(chord);

		Assert.True(SendKeysChord.TryParse(mapped, out var readBack));
		Assert.Equal(expected, readBack.Key);
	}
}
