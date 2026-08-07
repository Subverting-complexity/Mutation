using System;
using System.Linq;
using Mutation.Ui;

namespace Mutation.Tests;

/// <summary>
/// The third-party interaction dropdown is bound to these. A screen reader reads whatever is
/// in them, so an entry that falls back to the identifier is a blind user hearing
/// "DoNotInsert" (issue #243).
/// </summary>
public class DictationInsertOptionItemTests
{
	[Theory]
	[InlineData(DictationInsertOption.DoNotInsert, "Don't insert into 3rd party application")]
	[InlineData(DictationInsertOption.SendKeys, "Send keys to 3rd party application")]
	[InlineData(DictationInsertOption.Paste, "Paste into 3rd party application")]
	public void Describe_reads_the_human_wording(DictationInsertOption option, string expected)
	{
		Assert.Equal(expected, DictationInsertOptionItem.Describe(option));
	}

	// The point of the type: no entry may read out as its identifier.
	[Fact]
	public void No_option_is_left_reading_as_its_identifier()
	{
		foreach (DictationInsertOptionItem item in DictationInsertOptionItem.All())
		{
			Assert.NotEqual(item.Option.ToString(), item.Description);
			Assert.False(string.IsNullOrWhiteSpace(item.Description));
		}
	}

	[Fact]
	public void All_lists_every_option_once_in_declaration_order()
	{
		DictationInsertOption[] expected = Enum.GetValues<DictationInsertOption>();

		Assert.Equal(expected, DictationInsertOptionItem.All().Select(item => item.Option));
	}

	// The ComboBox displays the bound property, but anything that asks the item itself — an
	// automation client walking the list, a debugger, a log line — must not get the identifier.
	[Fact]
	public void ToString_gives_the_description_rather_than_the_record_shape()
	{
		var item = new DictationInsertOptionItem(DictationInsertOption.Paste, "Paste into 3rd party application");

		Assert.Equal("Paste into 3rd party application", item.ToString());
	}
}
