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

	// A human-written chord may spell the group out with parentheses. The '+' before
	// '(' is the separator, not the SendKeys Shift modifier, so the chord must be
	// translated rather than passed through (issue #225).
	[Theory]
	[InlineData("Ctrl+(AB)", "^(ab)")]
	[InlineData("Ctrl+Shift+(AB)", "^+(ab)")]
	[InlineData("Shift+(AB)", "+(ab)")]
	[InlineData("Alt+(ab)", "%(ab)")]
	[InlineData("(AB)", "(ab)")]
	public void Parenthesised_Group_In_Human_Chord_Is_Mapped(string input, string expected)
	{
		Assert.Equal(expected, SendKeysMapper.Map(input));
	}

	[Fact]
	public void Parenthesised_Group_Matches_Plus_Separated_Equivalent()
	{
		Assert.Equal(SendKeysMapper.Map("Ctrl+A+B"), SendKeysMapper.Map("Ctrl+(AB)"));
	}

	// A group may also spell its keys by name, so "Ctrl+(Enter)" means Ctrl+Enter
	// rather than the five letters e-n-t-e-r typed into the user's target app.
	[Theory]
	[InlineData("Ctrl+(Enter)", "^{ENTER}")]
	[InlineData("Ctrl+(F5)", "^{F5}")]
	[InlineData("Ctrl+(Enter Tab)", "^({ENTER}{TAB})")]
	[InlineData("Ctrl+(A B)", "^(ab)")]
	[InlineData("Ctrl+(\"a\")", "^a")]
	public void Named_Keys_Inside_A_Group_Resolve_By_Name(string input, string expected)
	{
		Assert.Equal(expected, SendKeysMapper.Map(input));
	}

	[Fact]
	public void Unsupported_Key_Inside_A_Group_Throws()
	{
		Assert.Throws<NotSupportedException>(() => SendKeysMapper.Map("Ctrl+(Win)"));
	}

	// Rejected rather than guessed at: there is no unambiguous per-character reading of
	// a symbol run, and inventing one would emit keystrokes the user never asked for.
	[Theory]
	[InlineData("Ctrl+(A(B)")]
	[InlineData("Ctrl+(A-B)")]
	[InlineData("Ctrl+(A+)")]
	public void Malformed_Or_Unreadable_Group_Is_Rejected(string input)
	{
		Assert.Throws<FormatException>(() => SendKeysMapper.Map(input));
	}

	// The compact form is one key per character by definition, so an unrecognised run
	// of letters expands rather than resolving as a name. Pinned so the rule is a
	// decision rather than an accident: separate the keys ("Ctrl+(Foo Key)") or name
	// them individually to get anything else.
	[Fact]
	public void Compact_Group_Expands_Per_Character_Even_For_Word_Like_Runs()
	{
		Assert.Equal("^(fookey)", SendKeysMapper.Map("Ctrl+(FooKey)"));
	}

	// The '+' in "a+(bc)" is not preceded by a modifier name, so it is SendKeys' Shift
	// modifier and the string must still pass through with the Shift intact.
	[Theory]
	[InlineData("a+(bc)")]
	[InlineData("{ENTER}+(ab)")]
	public void Plus_Group_Not_Following_A_Modifier_Name_Still_Passes_Through(string input)
	{
		Assert.Equal(input, SendKeysMapper.Map(input));
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

	// Only genuine SendKeys syntax passes through untouched. "^+(ab)" qualifies: its
	// leading '^' and the '+' that directly precedes '(' are both modifiers.
	[Theory]
	[InlineData("^a")]
	[InlineData("%{F4}")]
	[InlineData("~")]
	[InlineData("{ENTER}")]
	[InlineData("^+(ab)")]
	[InlineData("+(ab)")]
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
