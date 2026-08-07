using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Covers which side a dictated symbol closes up against. The formatter asks this once per
/// rule and then trusts it, so a symbol in the wrong class is wrong in every transcript that
/// uses that rule.
/// </summary>
public class SymbolAttachmentsTests
{
	[Theory]
	[InlineData(".")]
	[InlineData(",")]
	[InlineData(";")]
	[InlineData(":")]
	[InlineData("!")]
	[InlineData("?")]
	[InlineData(")")]
	[InlineData("]")]
	[InlineData("}")]
	[InlineData("%")]
	[InlineData("’")]
	[InlineData("”")]
	public void Closing_punctuation_attaches_on_the_left(string replaceWith)
	{
		Assert.Equal(SymbolAttachment.Left, SymbolAttachments.Classify(replaceWith));
	}

	[Theory]
	[InlineData("(")]
	[InlineData("[")]
	[InlineData("{")]
	[InlineData("$")]
	[InlineData("#")]
	[InlineData("‘")]
	[InlineData("“")]
	public void Opening_punctuation_attaches_on_the_right(string replaceWith)
	{
		Assert.Equal(SymbolAttachment.Right, SymbolAttachments.Classify(replaceWith));
	}

	[Theory]
	[InlineData("-")]
	[InlineData("/")]
	[InlineData("_")]
	[InlineData("&")]
	[InlineData("@")]
	[InlineData("+")]
	public void Connectors_attach_on_both_sides(string replaceWith)
	{
		Assert.Equal(SymbolAttachment.Both, SymbolAttachments.Classify(replaceWith));
	}

	// A straight quote does not say which end of the quotation it is. Guessing would be wrong
	// half the time, so it keeps the behaviour every symbol had before there were classes.
	[Theory]
	[InlineData("\"")]
	[InlineData("'")]
	public void Straight_quotes_are_left_alone(string replaceWith)
	{
		Assert.Equal(SymbolAttachment.Both, SymbolAttachments.Classify(replaceWith));
	}

	// The shipped defaults carry their own spacing. Classification has to look past it,
	// otherwise the punctuation that matters is never reached.
	[Fact]
	public void Leading_whitespace_is_skipped()
	{
		Assert.Equal(SymbolAttachment.Left, SymbolAttachments.Classify(". "));
		Assert.Equal(SymbolAttachment.Both, SymbolAttachments.Classify("\r\n- "));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\r\n")]
	public void Nothing_to_classify_falls_back_to_both(string? replaceWith)
	{
		Assert.Equal(SymbolAttachment.Both, SymbolAttachments.Classify(replaceWith));
	}
}
