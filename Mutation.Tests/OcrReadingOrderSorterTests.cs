using CognitiveSupport;

namespace Mutation.Tests;

// Issue #217: OcrReadingOrder was threaded all the way to the Azure call and then never
// applied, so the "basic" and "natural" hotkeys produced byte-identical output.
public class OcrReadingOrderSorterTests
{
	private static OcrTextLine Line(string text, double left, double top, double width = 100, double height = 20)
		=> new(text, left, top, left + width, top + height);

	private static string Sorted(IReadOnlyList<OcrTextLine> lines, OcrReadingOrder order)
		=> OcrReadingOrderSorter.ToText(OcrReadingOrderSorter.Sort(lines, order));

	// A two-column page: the API returns each column as a block, top to bottom.
	private static readonly OcrTextLine[] TwoColumnPage =
	[
		Line("left one", left: 0, top: 0),
		Line("left two", left: 0, top: 30),
		Line("left three", left: 0, top: 60),
		Line("right one", left: 200, top: 0),
		Line("right two", left: 200, top: 30),
		Line("right three", left: 200, top: 60),
	];

	// A 3x2 table, returned column-major the way a column-aware reader groups it.
	private static readonly OcrTextLine[] Table =
	[
		Line("Item", left: 0, top: 0, width: 80),
		Line("Widget", left: 0, top: 25, width: 80),
		Line("Gadget", left: 0, top: 50, width: 80),
		Line("Qty", left: 100, top: 0, width: 40),
		Line("2", left: 100, top: 25, width: 40),
		Line("7", left: 100, top: 50, width: 40),
		Line("Price", left: 160, top: 0, width: 60),
		Line("1.50", left: 160, top: 25, width: 60),
		Line("9.99", left: 160, top: 50, width: 60),
	];

	[Fact]
	public void LeftToRightTopToBottom_ReadsATwoColumnPageStraightAcross()
	{
		var text = Sorted(TwoColumnPage, OcrReadingOrder.LeftToRightTopToBottom);

		var expected = new[] { "left one", "right one", "left two", "right two", "left three", "right three" };
		Assert.Equal(string.Join(Environment.NewLine, expected) + Environment.NewLine, text);
	}

	[Fact]
	public void TopToBottomColumnAware_KeepsTheApiColumnOrder()
	{
		var text = Sorted(TwoColumnPage, OcrReadingOrder.TopToBottomColumnAware);

		Assert.Equal(
			string.Join(Environment.NewLine, TwoColumnPage.Select(l => l.Text)) + Environment.NewLine,
			text);
	}

	[Fact]
	public void TheTwoReadingOrders_ProduceDifferentOutput()
	{
		// The whole point of issue #217: these two hotkeys must not be interchangeable.
		Assert.NotEqual(
			Sorted(TwoColumnPage, OcrReadingOrder.LeftToRightTopToBottom),
			Sorted(TwoColumnPage, OcrReadingOrder.TopToBottomColumnAware));
	}

	[Fact]
	public void LeftToRightTopToBottom_ReadsATableRowByRow()
	{
		var text = Sorted(Table, OcrReadingOrder.LeftToRightTopToBottom);

		var expected = new[] { "Item", "Qty", "Price", "Widget", "2", "1.50", "Gadget", "7", "9.99" };
		Assert.Equal(string.Join(Environment.NewLine, expected) + Environment.NewLine, text);
	}

	[Fact]
	public void LeftToRightTopToBottom_KeepsSlightlyMisalignedCellsOnTheSameRow()
	{
		// Real OCR bounds for a table row vary by a few pixels between cells.
		var lines = new[]
		{
			Line("b", left: 100, top: 3),
			Line("a", left: 0, top: 0),
			Line("c", left: 200, top: 2),
		};

		var text = Sorted(lines, OcrReadingOrder.LeftToRightTopToBottom);

		Assert.Equal(string.Join(Environment.NewLine, "a", "b", "c") + Environment.NewLine, text);
	}

	[Fact]
	public void LeftToRightTopToBottom_KeepsConsecutiveParagraphLinesOnSeparateRows()
	{
		// Tightly-set body text: each line abuts the previous one but is still its own row.
		var lines = new[]
		{
			Line("first", left: 0, top: 0, height: 20),
			Line("second", left: 0, top: 20, height: 20),
			Line("third", left: 0, top: 40, height: 20),
		};

		var text = Sorted(lines, OcrReadingOrder.LeftToRightTopToBottom);

		Assert.Equal(string.Join(Environment.NewLine, "first", "second", "third") + Environment.NewLine, text);
	}

	[Fact]
	public void LeftToRightTopToBottom_ATallLineDoesNotSwallowTheRowsBesideIt()
	{
		// An invoice with a full-height sidebar, logo caption, or merged multi-line cell
		// next to normal rows. A row band that grew to the union of its members let the
		// tall line glue every row it touched into one interleaved line.
		var lines = new[]
		{
			new OcrTextLine("sidebar", 0, 0, 40, 100),
			Line("row one left", left: 60, top: 10, height: 10),
			Line("row one right", left: 200, top: 10, height: 10),
			Line("row two left", left: 60, top: 40, height: 10),
			Line("row two right", left: 200, top: 40, height: 10),
			Line("row three left", left: 60, top: 70, height: 10),
			Line("row three right", left: 200, top: 70, height: 10),
		};

		var text = Sorted(lines, OcrReadingOrder.LeftToRightTopToBottom);
		var rows = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

		// Each row's two cells must stay adjacent and in left-to-right order.
		Assert.Equal("row one left", rows[Array.IndexOf(rows, "row one right") - 1]);
		Assert.Equal("row two left", rows[Array.IndexOf(rows, "row two right") - 1]);
		Assert.Equal("row three left", rows[Array.IndexOf(rows, "row three right") - 1]);

		// And the rows must stay in top-to-bottom order relative to one another.
		Assert.True(Array.IndexOf(rows, "row one left") < Array.IndexOf(rows, "row two left"));
		Assert.True(Array.IndexOf(rows, "row two left") < Array.IndexOf(rows, "row three left"));
	}

	[Fact]
	public void LeftToRightTopToBottom_ATallCellDoesNotMergeTheFollowingRow()
	{
		// A double-height cell in the first row must not pull the second row up into it.
		var lines = new[]
		{
			Line("a", left: 0, top: 0, height: 20),
			Line("tall b", left: 100, top: 0, height: 40),
			Line("c", left: 0, top: 25, height: 20),
		};

		var text = Sorted(lines, OcrReadingOrder.LeftToRightTopToBottom);

		Assert.Equal(string.Join(Environment.NewLine, "a", "tall b", "c") + Environment.NewLine, text);
	}

	[Fact]
	public void Sort_LeavesASingleLineAlone()
	{
		var lines = new[] { Line("only", left: 5, top: 5) };

		Assert.Equal("only" + Environment.NewLine, Sorted(lines, OcrReadingOrder.LeftToRightTopToBottom));
	}

	[Fact]
	public void Sort_HandlesAnEmptyResult()
	{
		Assert.Equal(string.Empty, Sorted(Array.Empty<OcrTextLine>(), OcrReadingOrder.LeftToRightTopToBottom));
		Assert.Equal(string.Empty, Sorted(Array.Empty<OcrTextLine>(), OcrReadingOrder.TopToBottomColumnAware));
	}

	[Fact]
	public void Sort_TolerantOfZeroSizedBounds()
	{
		// A degenerate polygon must not collapse the page onto one row.
		var lines = new[]
		{
			new OcrTextLine("second", 0, 10, 0, 10),
			new OcrTextLine("first", 0, 0, 0, 0),
		};

		var text = Sorted(lines, OcrReadingOrder.LeftToRightTopToBottom);

		Assert.Equal(string.Join(Environment.NewLine, "first", "second") + Environment.NewLine, text);
	}
}
