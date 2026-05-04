using CognitiveSupport.Extensions;

namespace Mutation.Tests;

public class StringExtensionsTests
{
	[Fact]
	public void FixNewLines_NullInput_ReturnsNull()
	{
		string? input = null;
		Assert.Null(input!.FixNewLines());
	}

	[Fact]
	public void FixNewLines_EmptyString_ReturnsEmpty()
	{
		Assert.Equal(string.Empty, string.Empty.FixNewLines());
	}

	[Fact]
	public void FixNewLines_NoNewlines_ReturnsInputUnchanged()
	{
		Assert.Equal("hello world", "hello world".FixNewLines());
	}

	[Fact]
	public void FixNewLines_CrLf_NormalizesToEnvironmentNewLine()
	{
		string input = "line1\r\nline2\r\nline3";
		string expected = "line1" + Environment.NewLine + "line2" + Environment.NewLine + "line3";
		Assert.Equal(expected, input.FixNewLines());
	}

	[Fact]
	public void FixNewLines_LoneCr_NormalizesToEnvironmentNewLine()
	{
		string input = "line1\rline2\rline3";
		string expected = "line1" + Environment.NewLine + "line2" + Environment.NewLine + "line3";
		Assert.Equal(expected, input.FixNewLines());
	}

	[Fact]
	public void FixNewLines_LoneLf_NormalizesToEnvironmentNewLine()
	{
		string input = "line1\nline2\nline3";
		string expected = "line1" + Environment.NewLine + "line2" + Environment.NewLine + "line3";
		Assert.Equal(expected, input.FixNewLines());
	}

	[Fact]
	public void FixNewLines_MixedSeparators_AllNormalized()
	{
		string input = "a\r\nb\rc\nd";
		string expected = string.Join(Environment.NewLine, new[] { "a", "b", "c", "d" });
		Assert.Equal(expected, input.FixNewLines());
	}

	[Fact]
	public void FixNewLines_TrailingCrLf_PreservesTrailingNewline()
	{
		string input = "hello\r\n";
		string expected = "hello" + Environment.NewLine;
		Assert.Equal(expected, input.FixNewLines());
	}

	[Fact]
	public void FixNewLines_LeadingCrLf_PreservesLeadingNewline()
	{
		string input = "\r\nhello";
		string expected = Environment.NewLine + "hello";
		Assert.Equal(expected, input.FixNewLines());
	}

	[Fact]
	public void FixNewLines_OnlyCrLf_ReturnsSingleEnvironmentNewLine()
	{
		Assert.Equal(Environment.NewLine, "\r\n".FixNewLines());
	}

	[Fact]
	public void FixNewLines_AdjacentCrLfPairs_ProducesEmptyLineBetween()
	{
		string input = "a\r\n\r\nb";
		string expected = "a" + Environment.NewLine + Environment.NewLine + "b";
		Assert.Equal(expected, input.FixNewLines());
	}
}
