using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="Hotkey.Canonicalize"/>, the one spelling written back to settings.
/// <para>
/// The hotkey editor and the hotkey router row each used to split, upper-case and rejoin,
/// which keeps the order the user typed. So someone who typed <c>SHIFT+CTRL+A</c> by hand
/// had <c>SHIFT+CTRL+A</c> on disk while the rest of the app spelled that chord
/// <c>CTRL+SHIFT+A</c> (issue #323).
/// </para>
/// <para>
/// The half-typed cases matter as much as the canonical ones: text is normalized on every
/// commit, and a fallback that dropped what the user was still typing would erase the box
/// while they were looking away from it.
/// </para>
/// </summary>
public class HotkeyCanonicalizeTests
{
	[Theory]
	[InlineData("SHIFT+CTRL+A", "CTRL+SHIFT+A")]
	[InlineData("alt+ctrl+delete", "CTRL+ALT+DELETE")]
	[InlineData("win+shift+s", "SHIFT+WIN+S")]
	[InlineData("A+Windows+Control", "CTRL+WIN+A")]
	public void A_chord_comes_back_in_the_apps_own_order(string typed, string expected)
	{
		Assert.Equal(expected, Hotkey.Canonicalize(typed));
	}

	[Theory]
	[InlineData("Ctrl+C")]
	[InlineData("CTRL+SHIFT+F5")]
	[InlineData("CTRL+ALT+WIN+A")]
	public void Text_already_canonical_is_left_as_it_is(string text)
	{
		Assert.Equal(text.ToUpperInvariant(), Hotkey.Canonicalize(text));
	}

	[Theory]
	[InlineData("ctrl-c", "CTRL+C")]
	[InlineData("ctrl c", "CTRL+C")]
	[InlineData("shift/ctrl/a", "CTRL+SHIFT+A")]
	public void Any_separator_the_parser_accepts_comes_back_as_a_plus(string typed, string expected)
	{
		Assert.Equal(expected, Hotkey.Canonicalize(typed));
	}

	[Fact]
	public void A_digit_key_keeps_the_digit_rather_than_its_Number_alias()
	{
		Assert.Equal("CTRL+5", Hotkey.Canonicalize("ctrl+5"));
	}

	[Theory]
	[InlineData("Ctrl+", "CTRL")]
	[InlineData("ctrl+shift+", "CTRL+SHIFT")]
	[InlineData("Ctrl+Foo", "CTRL+FOO")]
	[InlineData("not-a-real-hotkey", "NOT+A+REAL+HOTKEY")]
	public void Half_typed_text_is_upper_cased_and_otherwise_kept(string typed, string expected)
	{
		// It cannot be parsed, so there is no canonical spelling for it. Keeping what the user
		// has typed leaves the validation message to say what is wrong with it, rather than
		// emptying the box under them.
		Assert.Equal(expected, Hotkey.Canonicalize(typed));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Nothing_typed_stays_nothing(string? typed)
	{
		Assert.Equal(string.Empty, Hotkey.Canonicalize(typed));
	}

	[Fact]
	public void A_modifier_on_its_own_is_never_reported_as_the_none_placeholder()
	{
		// Hotkey.ToString() answers "(none)" for a chord with no key, which is a description
		// and not a spelling. It must not reach a settings file.
		Assert.Equal("SHIFT", Hotkey.Canonicalize("shift"));
	}

	[Fact]
	public void The_canonical_form_is_what_a_parsed_chord_prints_as()
	{
		string canonical = Hotkey.Canonicalize("shift+ctrl+alt+win+a");

		Assert.Equal(Hotkey.Parse("shift+ctrl+alt+win+a").ToString(), canonical);
		Assert.Equal(canonical, Hotkey.Canonicalize(canonical));
	}
}
