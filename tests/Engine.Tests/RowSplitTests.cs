namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

/// <summary>Cutting one row of a table across two pages.</summary>
/// <remarks>
/// Thirteen lines of the paragraph take 156 of the 180 points a page holds, leaving room for two lines of the
/// row that follows, so a four line cell is cut after its second line. Two lines is also what the orphan and
/// widow rules allow to be left behind and carried on, which is why four is the shortest cell worth cutting.
/// </remarks>
public sealed class RowSplitTests
{
    [Fact]
    public void RowTallerThanTheRoomLeft_IsCutBetweenTheLinesOfItsCell()
    {
        Composition composition = Typeset.Html($"{Filler(13)}<table><tr><td>{Typeset.Numbered(4, "cell")}</td></tr></table>");

        Assert.Equal(["cell1", "cell2"], [.. composition.Pages[0].Lines.TakeLast(2)]);
        Assert.Equal(["cell3", "cell4"], [.. composition.Pages[1].Lines]);
    }

    /// <summary>The short cell holds one line and the tall one two, and both fragments stand 24 points tall.</summary>
    [Fact]
    public void CellsOfACutRow_AreAllAsTallAsTheFragmentTheyBelongTo()
    {
        Page page = Typeset.Html(Pair).Pages[0];

        Assert.Equal([24, 24], [.. Heights(page)]);
    }

    /// <summary>The row goes on, so the column that ran out of content goes on with it, holding nothing.</summary>
    [Fact]
    public void ColumnThatRanOutOfContent_KeepsAnEmptyBoxOnTheContinuation()
    {
        Page page = Typeset.Html(Pair).Pages[1];

        Assert.Equal([24, 24], [.. Heights(page)]);
        Assert.Equal(["a3", "a4"], [.. page.Lines]);
    }

    /// <summary>A rule at the cut would fence off half a row, so neither fragment draws one there.</summary>
    [Fact]
    public void CutRow_DrawsNoRuleWhereItWasCut()
    {
        string html = $"{Filler(13)}<table><tr style='border:1pt solid #000'><td>{Typeset.Numbered(4, "cell")}</td></tr></table>";

        Composition composition = Typeset.Html(html);
        RenderBox above = Assert.Single(composition.Pages[0].Boxes, box => box.Element == "tr");
        RenderBox below = Assert.Single(composition.Pages[1].Boxes, box => box.Element == "tr");

        Assert.Equal([1, 0], [.. Rules(above)]);
        Assert.Equal([0, 1], [.. Rules(below)]);
    }

    /// <summary>Every cell of the half row, including the one whose content fitted, gives up the rule at the cut.</summary>
    /// <remarks>
    /// The cell that overran is the obvious one; the short cell beside it is the one that goes wrong. It was
    /// never cut itself, so nothing marks it unless the row marks all of its cells, and a rule drawn under it
    /// alone fences off half the grid while the row above it draws none.
    /// </remarks>
    [Fact]
    public void CellsOfACutRow_DrawNoRuleWhereTheRowWasCut()
    {
        Composition composition = Typeset.Html(Bordered);

        Assert.Equal([1, 1], [.. Cells(composition.Pages[0]).Select(cell => cell.Border.Top.Width)]);
        Assert.Equal([0, 0], [.. Cells(composition.Pages[0]).Select(cell => cell.Border.Bottom.Width)]);
        Assert.Equal([0, 0], [.. Cells(composition.Pages[1]).Select(cell => cell.Border.Top.Width)]);
        Assert.Equal([1, 1], [.. Cells(composition.Pages[1]).Select(cell => cell.Border.Bottom.Width)]);
    }

    /// <summary>Twelve lines leave room for the heading and two lines of the row, so the row is cut under it.</summary>
    /// <remarks>A heading names the columns of half a row exactly as it names the columns of a whole one.</remarks>
    [Fact]
    public void RowCutAcross_CarriesTheHeadingOntoTheContinuation()
    {
        string html = $"{Filler(12)}<table><thead><tr><th>head</th></tr></thead>"
            + $"<tbody><tr><td>{Typeset.Numbered(4, "cell")}</td></tr></tbody></table>";

        Composition composition = Typeset.Html(html);

        Assert.Equal(["head", "cell1", "cell2"], [.. composition.Pages[0].Lines.TakeLast(3)]);
        Assert.Equal(["head", "cell3", "cell4"], [.. composition.Pages[1].Lines]);
    }

    /// <summary>A table inside a cell of a cut row flows onto the next page with the row that holds it.</summary>
    /// <remarks>
    /// A cell is an ordinary block, so cutting the row cuts what the cell holds by the same rules, a table
    /// included. Until a row could be cut, a table inside a cell could not be broken at all and a document
    /// laid out that way had to be assembled a page at a time.
    /// </remarks>
    [Fact]
    public void TableInsideACutCell_FlowsOntoTheNextPage()
    {
        string inner = string.Concat(Enumerable.Range(1, 4).Select(number => $"<tr><td>inner{number}</td></tr>"));
        string html = $"{Filler(13)}<table><tr><td style='width:120pt'><table>{inner}</table></td>"
            + "<td style='width:60pt'>side</td></tr></table>";

        Composition composition = Typeset.Html(html);

        Assert.DoesNotContain(composition.Pages[0].Spans, span => span.Text == "inner3");
        Assert.Equal(["inner3", "inner4"], [.. composition.Pages[1].Lines]);
    }

    /// <summary>The promise a spanning cell carries: the rows it covers are neither parted nor cut across.</summary>
    /// <remarks>
    /// The cell belongs to rows this one cannot see, so cutting it here would set its top half beside a row
    /// whose other half stands on the page after. The whole table moves instead. The second row opens 12 points
    /// down, which is why its only line shares a baseline with the second line of the cell spanning both.
    /// </remarks>
    [Fact]
    public void RowHoldingASpanningCell_IsNotCutAcross()
    {
        string html = $"{Filler(13)}<table><tr><td rowspan='2'>{Typeset.Numbered(4, "s")}</td><td>x</td></tr><tr><td>y</td></tr></table>";

        Composition composition = Typeset.Html(html);

        Assert.DoesNotContain(composition.Pages[0].Spans, span => span.Text == "s1");
        Assert.Equal(["s1x", "s2y", "s3", "s4"], [.. composition.Pages[1].Lines]);
    }

    private static string Pair =>
        $"{Filler(13)}<table><tr><td style='width:90pt'>{Typeset.Numbered(4, "a")}</td><td style='width:90pt'>b</td></tr></table>";

    /// <summary>Twelve filler lines, because a bordered cell needs a point more room at each edge to be cut.</summary>
    private static string Bordered =>
        $"{Filler(12)}<table><tr>"
        + $"<td style='width:90pt;border:1pt solid #000'>{Typeset.Numbered(4, "a")}</td>"
        + "<td style='width:90pt;border:1pt solid #000'>b</td></tr></table>";

    private static string Filler(int lines) => $"<p style='margin:0'>{Typeset.Numbered(lines)}</p>";

    private static IEnumerable<RenderBox> Cells(Page page) => page.Boxes.Where(box => box.Element == "td");

    private static IEnumerable<float> Heights(Page page) =>
        page.Boxes.Where(box => box.Element == "td").Select(box => MathF.Round(box.Bounds.Height, 2));

    private static IEnumerable<float> Rules(RenderBox row) => [row.Border.Top.Width, row.Border.Bottom.Width];
}
