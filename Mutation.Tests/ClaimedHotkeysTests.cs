using System;
using System.Linq;
using CognitiveSupport;
using Mutation.Ui.Core;

namespace Mutation.Tests;

/// <summary>
/// The three lists that fill one registration table, gathered as one so a screen that shows
/// none of them can still say what a shortcut is already taken by (issue #340).
/// </summary>
public class ClaimedHotkeysTests
{
	private static Settings SettingsWith(params LlmSettings.LlmPrompt[] prompts)
	{
		var settings = new Settings
		{
			TextToSpeechSettings = new TextToSpeechSettings { SpeakClipboard = "CTRL+ALT+G" },
			LlmSettings = new LlmSettings(),
			HotKeyRouterSettings = new HotKeyRouterSettings(),
		};

		settings.LlmSettings.Prompts.AddRange(prompts);
		return settings;
	}

	private static LlmSettings.LlmPrompt Prompt(string name, string? hotkey) =>
		new() { Name = name, Hotkey = hotkey };

	[Fact]
	public void The_app_s_own_shortcuts_are_in_it_under_the_names_the_page_shows()
	{
		var claimed = ClaimedHotkeys.Excluding(SettingsWith(), excludedPrompt: null);

		Assert.Contains(claimed, entry => entry.Name == "Speak clipboard" && entry.Text == "CTRL+ALT+G");
	}

	[Fact]
	public void A_send_key_row_is_carried_as_one_that_is_not_claimed_from_Windows()
	{
		var claimed = ClaimedHotkeys.Excluding(SettingsWith(), excludedPrompt: null);

		var sent = claimed.Single(entry => entry.Name == "Send key after OCR");
		Assert.False(sent.ClaimsTheChord);
	}

	[Fact]
	public void A_row_header_s_parenthesised_aside_is_dropped_before_it_is_read_out()
	{
		// "Send key after OCR (optional)" is the right header for a box that may be left blank,
		// and the wrong thing to hear inside "Already used by …".
		Assert.DoesNotContain(
			ClaimedHotkeys.Excluding(SettingsWith(), excludedPrompt: null),
			entry => entry.Name.Contains("(optional)", StringComparison.Ordinal));
	}

	[Fact]
	public void Every_route_and_every_prompt_is_in_it()
	{
		var settings = SettingsWith(Prompt("Summarize", "CTRL+ALT+P"));
		settings.HotKeyRouterSettings.Mappings.Add(new HotKeyRouterSettings.HotKeyRouterMap("CTRL+ALT+R", "F5"));

		var claimed = ClaimedHotkeys.Excluding(settings, excludedPrompt: null);

		Assert.Contains(claimed, entry => entry.Name == ClaimedHotkeys.RouteName && entry.Text == "CTRL+ALT+R");
		Assert.Contains(claimed, entry => entry.Text == "CTRL+ALT+P" && entry.Name.Contains("Summarize", StringComparison.Ordinal));
	}

	[Fact]
	public void The_prompt_being_edited_is_left_out_so_it_cannot_clash_with_itself()
	{
		var edited = Prompt("Summarize", "CTRL+ALT+P");
		var settings = SettingsWith(edited, Prompt("Tidy up", "CTRL+ALT+T"));

		var claimed = ClaimedHotkeys.Excluding(settings, edited);

		Assert.DoesNotContain(claimed, entry => entry.Text == "CTRL+ALT+P");
		Assert.Contains(claimed, entry => entry.Text == "CTRL+ALT+T");
	}

	[Fact]
	public void A_namesake_of_the_prompt_being_edited_is_still_in_it()
	{
		// By reference, not by name. Two prompts may be called the same thing, and dropping
		// both would hide a real clash behind a coincidence.
		var edited = Prompt("Summarize", "CTRL+ALT+P");
		var namesake = Prompt("Summarize", "CTRL+ALT+S");
		var settings = SettingsWith(edited, namesake);

		var claimed = ClaimedHotkeys.Excluding(settings, edited);

		Assert.Contains(claimed, entry => entry.Text == "CTRL+ALT+S");
	}

	[Fact]
	public void A_prompt_with_no_name_yet_is_still_referred_to_as_something()
	{
		Assert.Equal("an unnamed LLM prompt", ClaimedHotkeys.NameOf(Prompt("  ", "CTRL+ALT+P")));
	}

	[Fact]
	public void A_prompt_name_is_quoted_so_the_sentence_keeps_its_shape()
	{
		// A prompt's name is the user's own words and could be an ordinary phrase. Unquoted,
		// "Already used by the LLM prompt do the thing." reads as a sentence that lost its end.
		Assert.Equal("the LLM prompt \"do the thing\"", ClaimedHotkeys.NameOf(Prompt("do the thing", null)));
	}

	[Fact]
	public void Settings_with_no_prompts_and_no_routes_still_answers()
	{
		var bare = new Settings();

		var claimed = ClaimedHotkeys.Excluding(bare, excludedPrompt: null);

		Assert.Equal(CoreHotkeys.All.Length, claimed.Count);
	}

	[Fact]
	public void A_null_settings_object_is_a_mistake_rather_than_an_empty_answer()
	{
		Assert.Throws<ArgumentNullException>(() => ClaimedHotkeys.Excluding(null!, excludedPrompt: null));
	}
}
