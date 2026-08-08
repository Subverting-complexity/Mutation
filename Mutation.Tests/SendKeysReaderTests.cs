using Mutation.Ui.Services;
using Windows.System;

namespace Mutation.Tests;

/// <summary>
/// Issue #326: the "Send key after…" boxes accept Windows' SendKeys shorthand, and duplicate
/// detection could only read the plain spelling. These cover the reader that closes that gap.
/// <para>
/// Two directions matter equally. Missing a clash leaves the user where they already were.
/// Inventing one puts a "Duplicate hotkey" badge on a row that is perfectly fine, which is
/// worse — so the "reads nothing" cases below are not an afterthought.
/// </para>
/// </summary>
public class SendKeysReaderTests
{
	private static string[] Read(string? sendKeys) =>
		SendKeysReader.ChordsIn(sendKeys).Select(c => c.ToString()).ToArray();

	// ------------------------------------------------------------------
	// What it reads
	// ------------------------------------------------------------------

	[Fact]
	public void TheCaseFromTheIssueReadsBackAsTheChordItPresses()
	{
		Assert.Equal(["CTRL+F5"], Read("^{F5}"));
	}

	[Theory]
	[InlineData("^v", "CTRL+V")]
	[InlineData("%{TAB}", "ALT+TAB")]
	[InlineData("+{F5}", "SHIFT+F5")]
	[InlineData("^%{DEL}", "CTRL+ALT+DELETE")]
	[InlineData("^+%a", "CTRL+SHIFT+ALT+A")]
	[InlineData("^1", "CTRL+1")]
	public void AModifiedKeyReadsBackAsThatChord(string sendKeys, string expected)
	{
		Assert.Equal([expected], Read(sendKeys));
	}

	[Theory]
	[InlineData("{ENTER}", "ENTER")]
	[InlineData("{RETURN}", "ENTER")]
	[InlineData("{ESC}", "ESCAPE")]
	[InlineData("{BACKSPACE}", "BACK")]
	[InlineData("{PGDN}", "PAGEDOWN")]
	[InlineData("{PGUP}", "PAGEUP")]
	[InlineData("{APPS}", "APPLICATION")]
	[InlineData("{F12}", "F12")]
	[InlineData("{f12}", "F12")]
	[InlineData("~", "ENTER")]
	public void ANamedKeyIsAKeypressEvenWithNoModifier(string sendKeys, string expected)
	{
		// Unlike an ordinary character, a named key is never someone typing a word, so it
		// counts on its own — a Mutation shortcut bound to plain F12 really would clash.
		Assert.Equal([expected], Read(sendKeys));
	}

	[Fact]
	public void ASequenceReadsBackAsEveryChordInIt()
	{
		// What "Ctrl+V, Enter" turns into when the user writes it the other way.
		Assert.Equal(["CTRL+V", "ENTER"], Read("^v{ENTER}"));
	}

	[Fact]
	public void ARepeatCountIsTheSameKeyAndIsReadOnce()
	{
		Assert.Equal(["CTRL+LEFT"], Read("^{LEFT 5}"));
	}

	// ------------------------------------------------------------------
	// The group form
	// ------------------------------------------------------------------

	[Fact]
	public void AGroupHoldsItsModifiersDownAcrossEveryKeyInIt()
	{
		// "^+(ab)" is Ctrl+Shift+A and then Ctrl+Shift+B — two chords, not one naming both.
		Assert.Equal(["CTRL+SHIFT+A", "CTRL+SHIFT+B"], Read("^+(ab)"));
	}

	[Fact]
	public void AGroupCanHoldNamedKeysToo()
	{
		Assert.Equal(["CTRL+HOME", "CTRL+END"], Read("^({HOME}{END})"));
	}

	[Fact]
	public void AnUnmodifiedGroupIsJustTypingAndReadsNothing()
	{
		Assert.Empty(Read("(ab)"));
	}

	[Fact]
	public void AnUnclosedGroupReadsNothingAtAll()
	{
		// Not even the part before it: a string this broken is not one anybody should be
		// flagging rows from.
		Assert.Empty(Read("^{F5}^(ab"));
	}

	[Fact]
	public void ANestedGroupIsBeyondUsAndReadsNothing()
	{
		Assert.Empty(Read("^((ab))"));
	}

	// ------------------------------------------------------------------
	// Escaped literals — the reserved characters, written to be typed
	// ------------------------------------------------------------------

	[Theory]
	[InlineData("{+}")]
	[InlineData("{^}")]
	[InlineData("{%}")]
	[InlineData("{~}")]
	[InlineData("{(}")]
	[InlineData("{)}")]
	[InlineData("{{}")]
	[InlineData("{}}")]
	public void AnEscapedReservedCharacterIsATypedCharacterAndNotAModifier(string sendKeys)
	{
		// The whole point of the braces is that the character inside is not doing its
		// reserved job. Reading "{+}" as a Shift modifier, or "{}}" as an empty pair
		// followed by a stray brace, is how a fine row ends up flagged.
		Assert.Empty(Read(sendKeys));
	}

	[Fact]
	public void AnEscapedBraceDoesNotSwallowWhatFollowsIt()
	{
		Assert.Equal(["CTRL+F5"], Read("{}}^{F5}"));
	}

	[Fact]
	public void AnEscapedCaretDoesNotModifyWhatFollowsIt()
	{
		// "{^}^a" types a caret and then presses Ctrl+A. Only the second caret is a modifier.
		Assert.Equal(["CTRL+A"], Read("{^}^a"));
	}

	// ------------------------------------------------------------------
	// What it refuses to read
	// ------------------------------------------------------------------

	[Theory]
	[InlineData("hello")]
	[InlineData("Yours sincerely")]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	public void PlainTextIsSomethingBeingTypedAndReadsNothing(string? sendKeys)
	{
		Assert.Empty(Read(sendKeys));
	}

	[Fact]
	public void OnlyTheModifiedKeysInAMixedRunAreRead()
	{
		// "hi^v" types two letters and then presses Ctrl+V. Only the last one is a shortcut.
		Assert.Equal(["CTRL+V"], Read("hi^v"));
	}

	[Theory]
	[InlineData("^")]
	[InlineData("^+%")]
	[InlineData("^{F5}%")]
	public void AModifierWithNoKeyAfterItReadsNothingAtAll(string sendKeys)
	{
		Assert.Empty(Read(sendKeys));
	}

	[Theory]
	[InlineData("^{F5")]
	[InlineData("^{}")]
	[InlineData("^)")]
	[InlineData("^}")]
	public void AStringThatDoesNotHoldTogetherReadsNothingAtAll(string sendKeys)
	{
		Assert.Empty(Read(sendKeys));
	}

	[Fact]
	public void AnUnknownKeyNameIsSkippedWithoutLosingTheRest()
	{
		// The name is not one we can place, so it contributes nothing; the chord beside it
		// is still perfectly readable.
		Assert.Equal(["CTRL+F5"], Read("{NOTAKEY}^{F5}"));
	}

	[Fact]
	public void AKeyWeCannotSpellAsAChordSimplyDropsOut()
	{
		// A semicolon is a token separator to the hotkey parser, so "CTRL+;" is not a chord
		// it can express. Dropping out beats inventing something.
		Assert.Empty(Read("^;"));
	}

	// ------------------------------------------------------------------
	// Agreement with the rest of the app
	// ------------------------------------------------------------------

	[Fact]
	public void WhatSendKeysMapperWritesIsWhatThisReadsBack()
	{
		// The two directions have to agree, or a value the app produced would come back as
		// a different shortcut from the one the user asked for.
		string mapped = SendKeysMapper.Map("Ctrl+Shift+F5");

		Assert.Equal(["CTRL+SHIFT+F5"], Read(mapped));
	}

	[Fact]
	public void AChordReadsBackToTheSameKeyTheEditorWouldRegister()
	{
		var chord = Assert.Single(SendKeysReader.ChordsIn("%{F4}"));

		Assert.Equal(VirtualKey.F4, chord.Key);
		Assert.True(chord.Alt);
		Assert.False(chord.Control);
		Assert.False(chord.Shift);
		Assert.False(chord.Win);
	}
}
