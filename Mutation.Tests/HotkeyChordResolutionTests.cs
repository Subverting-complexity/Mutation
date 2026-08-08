using Mutation.Ui.Services;
using Windows.System;

namespace Mutation.Tests;

/// <summary>
/// Which shortcuts <see cref="HotkeyManager.SendHotkey"/> can put through SendInput, which
/// is the only path that reports whether the keystrokes were delivered.
/// <para>
/// Issue #325: anything that fell off this path went to the WinForms SendKeys fallback,
/// which answers nothing — so the shortcut sent after transcription and after OCR could
/// stop arriving with no beep, no message, and no log line saying so.
/// </para>
/// </summary>
public class HotkeyChordResolutionTests
{
	[Theory]
	[InlineData("^{DEL}", VirtualKey.Delete)]     // the shipped send-after setting
	[InlineData("%{F4}", VirtualKey.F4)]
	[InlineData("^+{TAB}", VirtualKey.Tab)]
	[InlineData("^c", VirtualKey.C)]
	public void SendKeysNotationResolvesToAChord(string text, VirtualKey expected)
	{
		var chord = HotkeyManager.ResolveChord(text);

		Assert.NotNull(chord);
		Assert.Equal(expected, chord.Key);
	}

	[Theory]
	[InlineData("CTRL+DELETE", VirtualKey.Delete)]
	[InlineData("Ctrl+V", VirtualKey.V)]
	[InlineData("SHIFT+ALT+U", VirtualKey.U)]
	[InlineData("WIN+D", VirtualKey.D)]
	public void TheOrdinarySpellingStillResolves(string text, VirtualKey expected)
	{
		var chord = HotkeyManager.ResolveChord(text);

		Assert.NotNull(chord);
		Assert.Equal(expected, chord.Key);
	}

	[Fact]
	public void CtrlDeleteResolvesTheSameWhicheverWayItIsWritten()
	{
		// Hotkey has value equality, so this compares the chords rather than their spellings.
		Assert.Equal(HotkeyManager.ResolveChord("CTRL+DELETE"), HotkeyManager.ResolveChord("^{DEL}"));
	}

	[Fact]
	public void CtrlVResolves()
	{
		// The chord the Paste insert option sends. When SendInput could not deliver it, the
		// transcript was announced as undelivered even though the fallback had pasted it.
		var chord = HotkeyManager.ResolveChord("Ctrl+V");

		Assert.NotNull(chord);
		Assert.True(chord.Control);
		Assert.Equal(VirtualKey.V, chord.Key);
	}

	[Theory]
	[InlineData("{ENTER}hello")]   // literal text to type
	[InlineData("^(ab)")]          // a group
	[InlineData("not a hotkey")]
	[InlineData("")]
	public void WhatItCannotExpressIsLeftToTheFallback(string text)
	{
		Assert.Null(HotkeyManager.ResolveChord(text));
	}
}
