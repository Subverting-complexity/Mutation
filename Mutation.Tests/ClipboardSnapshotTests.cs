using Mutation.Ui.Services;
using Xunit;

namespace Mutation.Tests;

public class ClipboardSnapshotTests
{
	[Fact]
	public void HasContent_False_WhenEmpty()
	{
		Assert.False(new ClipboardSnapshot().HasContent);
	}

	[Fact]
	public void HasContent_False_WhenTextEmptyAndImageZeroLength()
	{
		var snapshot = new ClipboardSnapshot { Text = "", PngImageBytes = new byte[0] };
		Assert.False(snapshot.HasContent);
	}

	[Fact]
	public void HasContent_True_WhenTextPresent()
	{
		Assert.True(new ClipboardSnapshot { Text = "hello" }.HasContent);
	}

	[Fact]
	public void HasContent_True_WhenImagePresent()
	{
		Assert.True(new ClipboardSnapshot { PngImageBytes = new byte[] { 1, 2, 3 } }.HasContent);
	}
}
