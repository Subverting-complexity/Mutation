using System;
using System.Collections.Generic;
using Mutation.Ui.Core;

namespace Mutation.Tests;

/// <summary>
/// What a prompt editor says when the shortcut being typed is already spoken for.
/// <para>
/// Two prompts could claim <c>CTRL+ALT+P</c> and both editors looked perfectly happy; the user
/// saved, and whichever registered second came back in the "Some hotkeys could not be
/// registered" dialog. That was the last list where a clash was still found out after the
/// fact, which is what #306 and #321 set out to remove everywhere else (issue #340).
/// </para>
/// <para>
/// The Hotkeys page can get away with a bare "Duplicate hotkey" because both rows are on the
/// screen in front of the user. Neither end of a prompt clash is, so what is pinned here is
/// mostly the naming: a warning that does not say what took the shortcut leaves the user with
/// nowhere to go (issue #336).
/// </para>
/// </summary>
public class HotkeyClashDescriptionTests
{
	private static readonly NamedHotkey SpeakClipboard = new("Speak clipboard", "CTRL+ALT+G", ClaimsTheChord: true);
	private static readonly NamedHotkey SummarizePrompt = new("the LLM prompt \"Summarize\"", "CTRL+ALT+P", ClaimsTheChord: true);
	private static readonly NamedHotkey Route = new(ClaimedHotkeys.RouteName, "CTRL+ALT+R", ClaimsTheChord: true);

	private static readonly NamedHotkey[] Everything = { SpeakClipboard, SummarizePrompt, Route };

	[Fact]
	public void A_free_shortcut_says_nothing()
	{
		Assert.Null(HotkeyClashDescription.For("CTRL+ALT+Z", Everything));
	}

	[Fact]
	public void A_shortcut_another_prompt_holds_names_that_prompt()
	{
		// The case nothing caught before this: neither end has a row on the Hotkeys page, so
		// neither screen had anything to flag.
		Assert.Equal(
			"Already used by the LLM prompt \"Summarize\".",
			HotkeyClashDescription.For("CTRL+ALT+P", Everything));
	}

	[Fact]
	public void A_shortcut_the_app_itself_holds_names_that_shortcut()
	{
		Assert.Equal(
			"Already used by Speak clipboard.",
			HotkeyClashDescription.For("CTRL+ALT+G", Everything));
	}

	[Fact]
	public void A_shortcut_a_route_listens_for_says_so()
	{
		Assert.Equal(
			$"Already used by {ClaimedHotkeys.RouteName}.",
			HotkeyClashDescription.For("CTRL+ALT+R", Everything));
	}

	[Fact]
	public void A_different_spelling_of_the_same_chord_is_the_same_chord()
	{
		// The reason the finder is reused rather than the text compared: Settings used to check
		// spellings and registration checked chords, so the two disagreed (issue #306).
		Assert.Equal(
			"Already used by Speak clipboard.",
			HotkeyClashDescription.For("alt+ctrl+g", Everything));
	}

	[Fact]
	public void Everything_holding_it_is_named_and_the_last_pair_is_joined_with_a_word()
	{
		var both = new[]
		{
			SpeakClipboard,
			SummarizePrompt with { Text = "CTRL+ALT+G" },
			Route with { Text = "CTRL+ALT+G" },
		};

		Assert.Equal(
			$"Already used by Speak clipboard, the LLM prompt \"Summarize\" and {ClaimedHotkeys.RouteName}.",
			HotkeyClashDescription.For("CTRL+ALT+G", both));
	}

	[Fact]
	public void Two_routes_on_one_chord_are_said_once()
	{
		var routes = new[] { Route, Route };

		Assert.Equal(
			$"Already used by {ClaimedHotkeys.RouteName}.",
			HotkeyClashDescription.For("CTRL+ALT+R", routes));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void A_shortcut_that_is_not_there_yet_cannot_be_taken(string? typed)
	{
		var blanks = new[] { SpeakClipboard with { Text = typed } };

		Assert.Null(HotkeyClashDescription.For(typed, blanks));
	}

	[Fact]
	public void Half_typed_text_is_not_a_chord_and_so_clashes_with_nothing()
	{
		// Both #320 exclusions, kept: text that cannot be registered cannot be claimed, and
		// reporting two unparseable values as duplicates of each other would be a second, wrong
		// complaint on top of the validation message they already carry.
		var halfTyped = new[] { SpeakClipboard with { Text = "CTRL+" } };

		Assert.Null(HotkeyClashDescription.For("CTRL+", halfTyped));
	}

	[Fact]
	public void A_key_that_is_only_sent_onward_still_counts_against_a_prompt()
	{
		// The other #320 exclusion is narrower than it looks. Two "Send key after…" values
		// holding the same key are not a conflict, because neither is claimed from Windows. A
		// prompt's shortcut is claimed — and Windows routes the synthesized keystroke straight
		// back to whoever claimed it, so the prompt would run itself.
		var sent = new[] { new NamedHotkey("Send key after OCR", "CTRL+ALT+P", ClaimsTheChord: false) };

		Assert.Equal(
			"Already used by Send key after OCR.",
			HotkeyClashDescription.For("CTRL+ALT+P", sent));
	}

	[Fact]
	public void Two_other_prompts_clashing_with_each_other_are_not_this_prompt_s_problem()
	{
		// The reason each candidate is asked about on its own. Answering the whole set at once
		// reports every entry that clashes with anything, which here would name two prompts the
		// shortcut in the box has nothing to do with.
		var others = new[]
		{
			SummarizePrompt with { Name = "the LLM prompt \"One\"", Text = "CTRL+ALT+1" },
			SummarizePrompt with { Name = "the LLM prompt \"Two\"", Text = "CTRL+ALT+1" },
		};

		Assert.Null(HotkeyClashDescription.For("CTRL+ALT+9", others));
	}

	[Fact]
	public void A_null_list_is_a_mistake_rather_than_an_empty_answer()
	{
		Assert.Throws<ArgumentNullException>(() =>
			HotkeyClashDescription.For("CTRL+ALT+G", (IReadOnlyList<NamedHotkey>)null!));
	}
}
