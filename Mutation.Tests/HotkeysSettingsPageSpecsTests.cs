using System.Linq;
using Mutation.Ui.Services;
using Mutation.Ui.Views.SettingsUi.Pages;

namespace Mutation.Tests;

/// <summary>
/// Covers the row table behind the Hotkeys settings page — which rows Mutation claims from
/// Windows, and which of them therefore accept SendKeys syntax.
/// <para>
/// The two "Send key after…" values are editable from two places, and only one of them used
/// to allow SendKeys syntax. So a <c>^{F5}</c> accepted on the OCR page showed a red
/// "Unsupported key" message on the Hotkeys page, read out as an error, on a setting that was
/// perfectly valid and working — on the page whose whole job is telling the user which
/// shortcuts are wrong (issue #322).
/// </para>
/// <para>
/// The page itself needs a WinUI host and cannot be built here (issue #304), so what is
/// pinned is the table the page reads and the validator it hands the flag to. The remaining
/// gap is one property assignment in <c>BuildRows</c>.
/// </para>
/// </summary>
public class HotkeysSettingsPageSpecsTests
{
	private const string SendKeyAfterOcr = "Send key after OCR (optional)";
	private const string SendKeyAfterTranscription = "Send key after transcription (optional)";
	private const string SendKeysValue = "^{F5}";

	private static HotkeysSettingsPage.HotkeySpec SpecFor(string label) =>
		HotkeysSettingsPage.Specs.Single(s => s.Label == label);

	[Theory]
	[InlineData(SendKeyAfterOcr)]
	[InlineData(SendKeyAfterTranscription)]
	public void A_send_key_row_takes_a_SendKeys_value_without_calling_it_an_error(string label)
	{
		var spec = SpecFor(label);

		Assert.True(spec.AllowsSendKeysSyntax);
		Assert.Null(HotkeyValidator.Validate(SendKeysValue, spec.AllowsSendKeysSyntax));
	}

	[Theory]
	[InlineData("Toggle microphone mute")]
	[InlineData("Speech to text")]
	[InlineData("Speak clipboard")]
	public void A_row_Mutation_claims_from_Windows_still_rejects_one(string label)
	{
		// The other direction matters just as much: a shortcut claimed from Windows has to be a
		// chord, and letting SendKeys text through there would fail at registration instead of
		// in the box, where the user can see it.
		var spec = SpecFor(label);

		Assert.False(spec.AllowsSendKeysSyntax);
		Assert.NotNull(HotkeyValidator.Validate(SendKeysValue, spec.AllowsSendKeysSyntax));
	}

	[Fact]
	public void Those_two_are_the_only_rows_that_accept_SendKeys_syntax()
	{
		Assert.Equal(
			new[] { SendKeyAfterOcr, SendKeyAfterTranscription },
			HotkeysSettingsPage.Specs.Where(s => s.AllowsSendKeysSyntax).Select(s => s.Label).ToArray());
	}

	[Fact]
	public void A_row_accepts_SendKeys_syntax_when_and_only_when_it_is_not_registered()
	{
		Assert.All(HotkeysSettingsPage.Specs,
			spec => Assert.Equal(!spec.Registers, spec.AllowsSendKeysSyntax));
	}

	[Fact]
	public void An_ordinary_chord_is_still_accepted_on_every_row()
	{
		Assert.All(HotkeysSettingsPage.Specs,
			spec => Assert.Null(HotkeyValidator.Validate("Ctrl+V", spec.AllowsSendKeysSyntax)));
	}

	[Fact]
	public void Only_those_two_rows_may_be_left_blank()
	{
		// A blank registered shortcut would leave an action with no way to reach it. These two
		// are the only ones the Clear button is meant for.
		Assert.Equal(
			new[] { SendKeyAfterOcr, SendKeyAfterTranscription },
			HotkeysSettingsPage.Specs.Where(s => s.AllowEmpty).Select(s => s.Label).ToArray());
	}
}
