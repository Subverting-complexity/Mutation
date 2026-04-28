using Mutation.Ui.Services;

namespace Mutation.Tests;

public class SendKeysMapperTests
{
	[Theory]
	[InlineData("Ctrl+V", "^v")]
	[InlineData("CTRL+v", "^v")]
	[InlineData("Ctrl+Delete", "^{DEL}")]
	[InlineData("Ctrl+Alt+Delete", "^%{DEL}")]
	[InlineData("Shift+F10", "+{F10}")]
	[InlineData("Alt+Space", "%{SPACE}")]
	[InlineData("Ctrl++", "^{+}")]
	[InlineData("Ctrl+C, Ctrl+V", "^c^v")]
	[InlineData("^{DEL}", "^{DEL}")]
	[InlineData("AltGr+E", "^%e")]
	[InlineData("PgDn", "{PGDN}")]
	[InlineData("ArrowUp", "{UP}")]
	public void Maps_Common_Inputs(string input, string expected)
	{
		Assert.Equal(expected, SendKeysMapper.Map(input));
	}

	[Fact]
	public void Throws_On_Unsupported_WindowsKey()
	{
		var ex = Assert.Throws<NotSupportedException>(() => SendKeysMapper.Map("Win+E"));
		Assert.Contains("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Throws_On_Unknown_Token()
	{
		var ex = Assert.Throws<FormatException>(() => SendKeysMapper.Map("Ctrl+FooKey"));
		Assert.Contains("Unknown key name", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Throws_On_Missing_Primary()
	{
		var ex = Assert.Throws<FormatException>(() => SendKeysMapper.Map("Ctrl+Shift"));
		Assert.Contains("No primary key", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	// ----- Function key boundaries -----

	[Theory]
	[InlineData("F1", "{F1}")]
	[InlineData("F12", "{F12}")]
	[InlineData("F24", "{F24}")]
	[InlineData("Ctrl+F5", "^{F5}")]
	public void Maps_Valid_Function_Keys(string input, string expected)
	{
		Assert.Equal(expected, SendKeysMapper.Map(input));
	}

	[Theory]
	[InlineData("F0")]
	[InlineData("F25")]
	[InlineData("F99")]
	public void Rejects_Out_Of_Range_Function_Keys(string input)
	{
		Assert.Throws<FormatException>(() => SendKeysMapper.Map(input));
	}

	// ----- All UnsupportedKeys -----

	[Theory]
	[InlineData("Win+E")]
	[InlineData("Windows+E")]
	[InlineData("Cmd+C")]
	[InlineData("Command+C")]
	[InlineData("Meta+C")]
	[InlineData("Super+C")]
	[InlineData("PrintScreen")]
	[InlineData("PrtSc")]
	[InlineData("PrtScr")]
	[InlineData("SysRq")]
	public void Throws_On_All_Unsupported_Keys(string input)
	{
		Assert.Throws<NotSupportedException>(() => SendKeysMapper.Map(input));
	}

	// ----- Reserved-char escaping (literal symbols via PLUS-style names) -----

	[Theory]
	[InlineData("Ctrl+Plus", "^{+}")]
	[InlineData("Ctrl+Caret", "^{^}")]
	[InlineData("Ctrl+Percent", "^{%}")]
	[InlineData("Ctrl+Tilde", "^{~}")]
	public void Escapes_Reserved_Symbol_Keys(string input, string expected)
	{
		Assert.Equal(expected, SendKeysMapper.Map(input));
	}

	// ----- Multi-key chord grouping -----

	[Fact]
	public void Multiple_Plain_Keys_In_One_Chord_Group_With_Parens()
	{
		Assert.Equal("^(ab)", SendKeysMapper.Map("Ctrl+A+B"));
	}

	[Fact]
	public void Multiple_Modifiers_With_Multiple_Keys_Group()
	{
		Assert.Equal("^+(ab)", SendKeysMapper.Map("Ctrl+Shift+A+B"));
	}

	// ----- Alternative modifier names -----

	[Theory]
	[InlineData("Ctl+A", "^a")]
	[InlineData("Opt+A", "%a")]
	[InlineData("Option+A", "%a")]
	[InlineData("Shft+A", "+a")]
	public void Recognizes_Alternative_Modifier_Names(string input, string expected)
	{
		Assert.Equal(expected, SendKeysMapper.Map(input));
	}

	// ----- Comma-split sequences -----

	[Theory]
	[InlineData("Ctrl+C , Ctrl+V", "^c^v")]
	[InlineData("Ctrl+A,Ctrl+C,Ctrl+V", "^a^c^v")]
	public void Comma_Split_Sequences_Concatenated(string input, string expected)
	{
		Assert.Equal(expected, SendKeysMapper.Map(input));
	}

	// ----- SendKeys passthrough -----

	[Theory]
	[InlineData("^a")]
	[InlineData("%{F4}")]
	[InlineData("~")]
	[InlineData("{ENTER}")]
	[InlineData("^+(ab)")]
	[InlineData("Ctrl+(AB)")]
	public void Passthrough_When_Already_SendKeys_Syntax(string input)
	{
		Assert.Equal(input, SendKeysMapper.Map(input));
	}

	// ----- Quoted single-char literal -----

	[Fact]
	public void Maps_Quoted_Letter_Literal()
	{
		Assert.Equal("^a", SendKeysMapper.Map("Ctrl+\"a\""));
	}

	// ----- KeyMap representative entries -----

	[Theory]
	[InlineData("Enter", "{ENTER}")]
	[InlineData("Return", "{ENTER}")]
	[InlineData("Tab", "{TAB}")]
	[InlineData("Esc", "{ESC}")]
	[InlineData("Backspace", "{BACKSPACE}")]
	[InlineData("Insert", "{INS}")]
	[InlineData("Home", "{HOME}")]
	[InlineData("End", "{END}")]
	[InlineData("PgUp", "{PGUP}")]
	[InlineData("CapsLock", "{CAPSLOCK}")]
	[InlineData("Backslash", "\\")]
	[InlineData("Comma", ",")]
	public void Maps_Representative_KeyMap_Entries(string input, string expected)
	{
		Assert.Equal(expected, SendKeysMapper.Map(input));
	}

	// ----- Null / empty guards -----

	[Fact]
	public void Map_NullInput_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => SendKeysMapper.Map(null!));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Map_EmptyOrWhitespace_Throws(string input)
	{
		Assert.Throws<ArgumentException>(() => SendKeysMapper.Map(input));
	}
}
