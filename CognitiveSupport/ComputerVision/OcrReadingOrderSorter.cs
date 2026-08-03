using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CognitiveSupport;

/// <summary>
/// One OCR line with the axis-aligned bounds of its detected polygon, in image pixels.
/// </summary>
public readonly record struct OcrTextLine(string Text, double Left, double Top, double Right, double Bottom)
{
	public double Height => Bottom - Top;
	public double VerticalCentre => (Top + Bottom) / 2.0;
}

/// <summary>
/// Applies the requested <see cref="OcrReadingOrder"/> to already-recognised lines.
///
/// Azure Image Analysis 4.0 has no reading-order request option (unlike the retired
/// Read 3.2 API), and returns lines grouped into blocks in its own natural,
/// column-aware order. <see cref="OcrReadingOrder.TopToBottomColumnAware"/> therefore
/// keeps that order as-is, while <see cref="OcrReadingOrder.LeftToRightTopToBottom"/>
/// re-sorts every line onto visual rows that run straight across the page — which is
/// what a table, spreadsheet, form, or invoice needs (issue #217).
/// </summary>
public static class OcrReadingOrderSorter
{
	// A line joins the row being built when its vertical span overlaps the row's by at
	// least this fraction of the shorter of the two. Generous enough that cells whose
	// baselines differ by a few pixels still share a row, strict enough that the next
	// line of a paragraph — which merely abuts the previous one — starts a new row.
	private const double RowOverlapFactor = 0.5;

	public static IReadOnlyList<OcrTextLine> Sort(
		IReadOnlyList<OcrTextLine> lines,
		OcrReadingOrder readingOrder)
	{
		ArgumentNullException.ThrowIfNull(lines);

		if (readingOrder != OcrReadingOrder.LeftToRightTopToBottom || lines.Count < 2)
			return lines;

		return GroupIntoRows(lines)
			.SelectMany(row => row.OrderBy(line => line.Left).ThenBy(line => line.Top))
			.ToList();
	}

	public static string ToText(IReadOnlyList<OcrTextLine> lines)
	{
		ArgumentNullException.ThrowIfNull(lines);

		var sb = new StringBuilder();
		foreach (var line in lines)
			sb.AppendLine(line.Text);
		return sb.ToString();
	}

	private static List<List<OcrTextLine>> GroupIntoRows(IReadOnlyList<OcrTextLine> lines)
	{
		// Ordering by vertical centre first means a row is always built from lines that
		// are adjacent vertically, whatever order the API returned them in.
		var byVerticalPosition = lines
			.OrderBy(line => line.VerticalCentre)
			.ThenBy(line => line.Left)
			.ToList();

		var rows = new List<List<OcrTextLine>>();
		var current = new List<OcrTextLine> { byVerticalPosition[0] };
		// The band stays pinned to the line that opened the row and is deliberately
		// never widened to the union of its members. Growing it let one abnormally tall
		// line — a sidebar, a logo caption, a merged multi-line cell — dilate the band
		// far enough to swallow several genuinely separate table rows, which then got
		// re-sorted by Left into one interleaved line of nonsense.
		double rowTop = byVerticalPosition[0].Top;
		double rowBottom = byVerticalPosition[0].Bottom;

		foreach (var line in byVerticalPosition.Skip(1))
		{
			if (SharesRow(rowTop, rowBottom, line))
			{
				current.Add(line);
				continue;
			}

			rows.Add(current);
			current = new List<OcrTextLine> { line };
			rowTop = line.Top;
			rowBottom = line.Bottom;
		}

		rows.Add(current);
		return rows;
	}

	private static bool SharesRow(double rowTop, double rowBottom, OcrTextLine line)
	{
		double overlap = Math.Min(rowBottom, line.Bottom) - Math.Max(rowTop, line.Top);
		if (overlap <= 0)
			return false;

		// Degenerate zero-height bounds would otherwise make the threshold zero and
		// collapse the whole page onto one row.
		double shorter = Math.Max(1.0, Math.Min(rowBottom - rowTop, line.Height));
		return overlap >= shorter * RowOverlapFactor;
	}
}
