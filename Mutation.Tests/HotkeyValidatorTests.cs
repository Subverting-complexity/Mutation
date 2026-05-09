using Mutation.Ui.Services;

namespace Mutation.Tests;

public class HotkeyValidatorTests
{
	[Theory]
	[InlineData("Ctrl+C")]
	[InlineData("Ctrl+Shift+F5")]
	[InlineData("Ctrl+Win+P")]
	public void ValidInBothModes(string input)
	{
		Assert.Null(HotkeyValidator.Validate(input, allowSendKeysSyntax: false));
		Assert.Null(HotkeyValidator.Validate(input, allowSendKeysSyntax: true));
	}

	[Theory]
	[InlineData("A{DEL}")]
	[InlineData("^c")]
	[InlineData("+(abc)")]
	[InlineData("Ctrl+C, A{DEL}")]
	public void SendKeysSyntax_OnlyValidWhenAllowed(string input)
	{
		Assert.NotNull(HotkeyValidator.Validate(input, allowSendKeysSyntax: false));
		Assert.Null(HotkeyValidator.Validate(input, allowSendKeysSyntax: true));
	}

	[Theory]
	[InlineData("Foo")]
	[InlineData("Ctrl+Foo")]
	public void Garbage_InvalidInBothModes(string input)
	{
		Assert.NotNull(HotkeyValidator.Validate(input, allowSendKeysSyntax: false));
		Assert.NotNull(HotkeyValidator.Validate(input, allowSendKeysSyntax: true));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Empty_ReturnsNull(string input)
	{
		Assert.Null(HotkeyValidator.Validate(input, allowSendKeysSyntax: false));
		Assert.Null(HotkeyValidator.Validate(input, allowSendKeysSyntax: true));
	}
}
