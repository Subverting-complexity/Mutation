using CognitiveSupport;
using Mutation.Ui.Services;

namespace Mutation.Tests;

public class HotkeyBindingFailureTests
{
	[Theory]
	[InlineData("Ctrl+C")]
	[InlineData("Ctrl+Shift+F5")]
	[InlineData("Ctrl+Alt+S")]
	public void ClassifyConfiguredHotkey_ValidHotkey_ReturnsNull(string input)
	{
		Assert.Null(HotkeyManager.ClassifyConfiguredHotkey("Some action", input, allowEmpty: true));
	}

	[Theory]
	[InlineData("NotAKey")]
	[InlineData("Ctrl+Shift")]
	[InlineData("Ctrl+Bogus")]
	public void ClassifyConfiguredHotkey_InvalidHotkey_ReturnsFailureWithReason(string input)
	{
		var failure = HotkeyManager.ClassifyConfiguredHotkey("Start/stop dictation", input, allowEmpty: true);

		Assert.NotNull(failure);
		Assert.Equal("Start/stop dictation", failure!.Description);
		Assert.Equal(input.Trim(), failure.Hotkey);
		Assert.False(string.IsNullOrWhiteSpace(failure.Reason));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void ClassifyConfiguredHotkey_EmptyWhenAllowed_ReturnsNull(string? input)
	{
		Assert.Null(HotkeyManager.ClassifyConfiguredHotkey("Optional action", input, allowEmpty: true));
	}

	[Fact]
	public void ClassifyConfiguredHotkey_EmptyWhenNotAllowed_ReturnsFailure()
	{
		var failure = HotkeyManager.ClassifyConfiguredHotkey("Required action", "", allowEmpty: false);

		Assert.NotNull(failure);
		Assert.Equal("Required action", failure!.Description);
		Assert.Equal("Enter a hotkey.", failure.Reason);
	}

	[Fact]
	public void BuildFailureMessage_EmptyList_ReturnsEmptyString()
	{
		Assert.Equal(string.Empty, HotkeyManager.BuildFailureMessage(new List<HotkeyManager.HotkeyBindingFailure>()));
	}

	[Fact]
	public void BuildFailureMessage_WithHotkey_IncludesDescriptionHotkeyAndReason()
	{
		var failures = new List<HotkeyManager.HotkeyBindingFailure>
		{
			new("Start/stop dictation", "Ctrl+Alt+S", "The shortcut is already registered by another application."),
		};

		var message = HotkeyManager.BuildFailureMessage(failures);

		Assert.Equal("• Start/stop dictation (Ctrl+Alt+S): The shortcut is already registered by another application.", message);
	}

	[Fact]
	public void BuildFailureMessage_WithoutHotkey_OmitsParentheses()
	{
		var failures = new List<HotkeyManager.HotkeyBindingFailure>
		{
			new("Ctrl+A → Ctrl+B", "", "Both hotkeys must be configured."),
		};

		var message = HotkeyManager.BuildFailureMessage(failures);

		Assert.Equal("• Ctrl+A → Ctrl+B: Both hotkeys must be configured.", message);
	}

	[Fact]
	public void BuildFailureMessage_MultipleFailures_OneLinePerFailure()
	{
		var failures = new List<HotkeyManager.HotkeyBindingFailure>
		{
			new("Action one", "Ctrl+1", "reason one"),
			new("Action two", "Ctrl+2", "reason two"),
		};

		var message = HotkeyManager.BuildFailureMessage(failures);

		var lines = message.Split(Environment.NewLine);
		Assert.Equal(2, lines.Length);
		Assert.Contains("Action one", lines[0]);
		Assert.Contains("Action two", lines[1]);
	}

	[Fact]
	public void ToBindingFailures_SkipsSuccessesAndKeepsFailures()
	{
		var okMap = new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+A", "Ctrl+B");
		var badMap = new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+D");
		var results = new List<HotkeyManager.HotkeyRegistrationResult>
		{
			new(okMap, "CTRL+A", true, 1, null),
			new(badMap, "CTRL+C", false, -1, "The shortcut is already registered by another application."),
		};

		var failures = HotkeyManager.ToBindingFailures(results);

		var failure = Assert.Single(failures);
		Assert.Equal("Ctrl+C → Ctrl+D", failure.Description);
		Assert.Equal(string.Empty, failure.Hotkey);
		Assert.Equal("The shortcut is already registered by another application.", failure.Reason);
	}

	[Fact]
	public void ToBindingFailures_MissingErrorMessage_UsesUnknownError()
	{
		var badMap = new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+D");
		var results = new List<HotkeyManager.HotkeyRegistrationResult>
		{
			new(badMap, "CTRL+C", false, -1, null),
		};

		var failure = Assert.Single(HotkeyManager.ToBindingFailures(results));
		Assert.Equal("Unknown error.", failure.Reason);
	}
}
