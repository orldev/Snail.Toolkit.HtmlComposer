namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class TableTests
{
    [Fact]
    public void Table_SetsTheCellsOfARowSideBySide()
    {
        Page page = Typeset.Html("<table><tr><td style='width:60pt'>a</td><td style='width:120pt'>b</td></tr></table>").Pages[0];
        var cells = page.Boxes.Where(box => box.Element == "td").ToList();

        Assert.Equal(10, cells[0].Bounds.X, 0.01);
        Assert.Equal(60, cells[0].Bounds.Width, 0.01);
        Assert.Equal(70, cells[1].Bounds.X, 0.01);
        Assert.Equal(120, cells[1].Bounds.Width, 0.01);
        Assert.Equal([10, 70], [.. page.Spans.Select(span => MathF.Round(span.X, 2))]);
    }

    /// <summary>Six characters fit the 36 point column, so the first cell takes two lines and the row is 24 tall.</summary>
    [Fact]
    public void Table_MakesEveryCellOfARowAsTallAsTheTallest()
    {
        Page page = Typeset.Html("<table><tr><td style='width:36pt'>abcde fghij</td><td style='width:144pt'>x</td></tr></table>").Pages[0];

        Assert.All(page.Boxes.Where(box => box.Element == "td"), cell => Assert.Equal(24, cell.Bounds.Height, 0.01));
    }

    /// <summary>
    /// «one two six twelve» is 18 characters unbroken and «twelve» is its longest word, so the column starts at
    /// 36, grows to 108, and the 60 points still free are split with the column beside it: 138 and 42.
    /// </summary>
    [Fact]
    public void ColumnsWithoutDeclaredWidths_GrowTowardsWhatTheirContentWants()
    {
        Page page = Typeset.Html("<table><tr><td>one two six twelve</td><td>ok</td></tr></table>").Pages[0];

        Assert.Equal([138, 42], [.. Widths(page)]);
    }

    [Fact]
    public void ColumnWithADeclaredWidth_KeepsItWhileTheRestTakesWhatIsLeft()
    {
        Page page = Typeset.Html("<table><tr><td style='width:60pt'>one two</td><td>ok</td></tr></table>").Pages[0];

        Assert.Equal([60, 120], [.. Widths(page)]);
    }

    /// <summary>
    /// The column arrays used to be as wide as whatever number the markup wrote: measured on this engine,
    /// <c>colspan="2000000"</c> cost 826 MB from one cell and <c>colspan="2147483647"</c> threw before a page
    /// could be set. Markup is often written by users, so an overlong span is capped and noted.
    /// </summary>
    [Theory]
    [InlineData("2000000")]
    [InlineData("2147483647")]
    public void CellSpanningMoreColumnsThanATableMayHold_IsCappedAndSaysSo(string colspan)
    {
        Composition composition = Typeset.Html($"<table><tr><td colspan='{colspan}'>wide</td></tr><tr><td>a</td><td>b</td></tr></table>");

        Assert.Single(composition.Pages);
        Assert.Equal("colspan", Assert.Single(composition.Notes, note => note.Kind == NoteKind.SpanCapped).Subject);
    }

    [Fact]
    public void CellSpanningMoreRowsThanATableMayHold_IsCappedLikeAColumnSpan()
    {
        Composition composition = Typeset.Html("<table><tr><td rowspan='9000000'>tall</td><td>a</td></tr></table>");

        Assert.Equal("rowspan", Assert.Single(composition.Notes, note => note.Kind == NoteKind.SpanCapped).Subject);
    }

    [Fact]
    public void CellSpanningWhatATableMayHold_IsLeftAlone()
    {
        Composition composition = Typeset.Html("<table><tr><td colspan='2'>wide</td></tr><tr><td>a</td><td>b</td></tr></table>");

        Assert.Empty(composition.Notes);
    }

    /// <summary>Two forty-character words need 480 points of a 180 point line, and neither may be set narrower.</summary>
    /// <remarks>
    /// Squeezing a column below the word it has to hold would not fit the word, it would only move the overrun
    /// out of sight. The table overruns instead, where it can be seen and the markup fixed.
    /// </remarks>
    [Fact]
    public void ColumnsTooNarrowForTheirLongestWords_KeepThemAndOverrun()
    {
        string word = new('x', 40);

        Page page = Typeset.Html($"<table><tr><td>{word}</td><td>{word}</td></tr></table>").Pages[0];

        Assert.Equal([240, 240], [.. Widths(page)]);
    }

    [Fact]
    public void Table_ColumnsWithoutADeclaredWidth_ShareWhatIsLeftEqually()
    {
        Page page = Typeset.Html("<table><tr><td>a</td><td>b</td><td>c</td></tr></table>").Pages[0];

        Assert.Equal([10, 70, 130], [.. page.Spans.Select(span => MathF.Round(span.X, 2))]);
    }

    [Fact]
    public void CellSpanningTwoColumns_IsAsWideAsBothOfThem()
    {
        const string html = "<table><tr><td colspan='2'>title</td></tr>"
            + "<tr><td style='width:60pt'>a</td><td style='width:120pt'>b</td></tr></table>";

        Page page = Typeset.Html(html).Pages[0];

        Assert.Equal([180, 60, 120], [.. Widths(page)]);
    }

    /// <summary>A thirty character word needs 180 points, and the two columns under it hold six each.</summary>
    /// <remarks>
    /// The spanning cell is measured after the columns have taken what their own cells asked for, and it adds
    /// only what they lack: 168 points shared between them. A heading given its say first would decide the
    /// shape of a table whose rows never needed the room.
    /// </remarks>
    [Fact]
    public void ColumnsUnderASpanningCell_AreWidenedOnlyByWhatTheyLack()
    {
        string word = new('a', 30);

        Page page = Typeset.Html($"<table><tr><td colspan='2'>{word}</td></tr><tr><td>a</td><td>b</td></tr></table>").Pages[0];

        Assert.Equal([180, 90, 90], [.. Widths(page)]);
    }

    /// <summary>A cell over two rows is as tall as both, and the row beneath steps over the column it holds.</summary>
    [Fact]
    public void CellSpanningTwoRows_CoversThemBothAndHoldsItsColumn()
    {
        const string html = "<table><tr><td rowspan='2' style='width:60pt'>a</td><td style='width:120pt'>b</td></tr>"
            + "<tr><td style='width:120pt'>c</td></tr></table>";

        Page page = Typeset.Html(html).Pages[0];

        Assert.Equal([24, 12, 12], [.. Heights(page)]);
        Assert.Equal([10, 70, 70], [.. Lefts(page)]);
    }

    /// <summary>Three lines of 12 need 36, and the two rows hold 24, so the last of them takes the other 12.</summary>
    [Fact]
    public void CellTallerThanTheRowsItSpans_GrowsTheLastOfThem()
    {
        const string html = "<table><tr><td rowspan='2' style='width:36pt'>abcde fghij klmno</td><td style='width:144pt'>b</td></tr>"
            + "<tr><td style='width:144pt'>c</td></tr></table>";

        Page page = Typeset.Html(html).Pages[0];

        Assert.Equal([36, 12, 24], [.. Heights(page)]);
    }

    /// <summary>Fourteen lines leave room for one row, and the row beneath it is held by the cell spanning both.</summary>
    /// <remarks>
    /// Parting them would leave the spanning cell painted over a page that ends and the rest of its row opening
    /// the next one without it. The pair moves together, the way a heading moves with its rows.
    /// </remarks>
    [Fact]
    public void RowsHeldBySpanningCell_AreNotPartedByThePageEnd()
    {
        string html = $"<p style='margin:0'>{Typeset.Numbered(14)}</p>"
            + "<table><tr><td rowspan='2'>a</td><td>b</td></tr><tr><td>c</td></tr></table>";

        Composition composition = Typeset.Html(html);

        Assert.DoesNotContain(composition.Pages[0].Spans, span => span.Text == "b");
        Assert.Equal(["ab", "c"], composition.Pages[1].Lines);
    }

    /// <summary>A 24pt line sits on a baseline 19.2 down and a 12pt line 9.6 down, so the smaller drops 9.6.</summary>
    /// <remarks>
    /// A label beside a number twice its size reads as one line only when the two sit on one baseline. CSS
    /// gives a table cell that by default, and so does this: the cells start level only when their text does.
    /// </remarks>
    [Fact]
    public void CellsOfARow_SitOnOneBaselineByDefault()
    {
        Page page = Typeset.Html("<table><tr><td style='width:90pt;font-size:24pt'>a</td><td style='width:90pt'>b</td></tr></table>").Pages[0];

        Assert.Equal([29.2, 29.2], [.. page.Spans.Select(span => Math.Round(span.Baseline, 2))]);
    }

    /// <summary>Three lines make the row 36 tall, so a one-line cell has 24 points to give away.</summary>
    [Fact]
    public void CellAlignedMiddle_HoldsItsContentHalfwayDownTheRow()
    {
        Page page = Typeset.Html(Row("middle")).Pages[0];

        Assert.Equal(31.6, Assert.Single(page.Spans, span => span.Text == "x").Baseline, 0.01);
        Assert.All(page.Boxes.Where(box => box.Element == "td"), cell => Assert.Equal(36, cell.Bounds.Height, 0.01));
    }

    [Fact]
    public void CellAlignedBottom_HoldsItsContentAtTheFootOfTheRow()
    {
        Page page = Typeset.Html(Row("bottom")).Pages[0];

        Assert.Equal(43.6, Assert.Single(page.Spans, span => span.Text == "x").Baseline, 0.01);
    }

    [Fact]
    public void CellWithoutAnAlignment_HoldsItsContentAtTheTopOfTheRow()
    {
        Page page = Typeset.Html(Row(align: null)).Pages[0];

        Assert.Equal(19.6, Assert.Single(page.Spans, span => span.Text == "x").Baseline, 0.01);
    }

    /// <summary>Fifteen 12 point rows fill the 180 point page, so the thirtieth row lands on the second.</summary>
    [Fact]
    public void Table_TallerThanThePage_IsCutBetweenItsRows()
    {
        Composition composition = Typeset.Html($"<table>{Rows(30)}</table>");

        Assert.Equal(2, composition.Pages.Count);
        Assert.Equal("15", composition.Pages[0].Lines[^1]);
        Assert.Equal("16", composition.Pages[1].Lines[0]);
    }

    [Fact]
    public void TableCutBetweenPages_RepeatsItsHeadingOnEveryFragment()
    {
        Composition composition = Typeset.Html($"<table><thead><tr><th>title</th></tr></thead><tbody>{Rows(20)}</tbody></table>");

        Assert.True(composition.Pages.Count > 1);
        Assert.All(composition.Pages, page => Assert.Contains(page.Spans, span => span.Text == "title"));
    }

    /// <summary>Fourteen lines leave exactly one row of room, which the heading must not take on its own.</summary>
    [Fact]
    public void TableHeading_IsNeverLeftAloneAtTheFootOfAPage()
    {
        string html = $"<p style='margin:0'>{Typeset.Numbered(14)}</p>"
            + $"<table><thead><tr><th>title</th></tr></thead><tbody>{Rows(4)}</tbody></table>";

        Composition composition = Typeset.Html(html);

        Assert.DoesNotContain(composition.Pages[0].Spans, span => span.Text == "title");
        Assert.Equal("title", composition.Pages[1].Lines[0]);
    }

    /// <summary>A rule under a row is drawn across the table, not under one cell of it.</summary>
    [Fact]
    public void RowBorder_IsPaintedAcrossTheWholeRow()
    {
        string html = "<table><tr style='border-bottom:1pt solid #000'><td style='width:60pt'>a</td><td style='width:120pt'>b</td></tr></table>";

        RenderBox row = Assert.Single(Typeset.Html(html).Pages[0].Boxes, box => box.Element == "tr");

        Assert.Equal(10, row.Bounds.X, 0.01);
        Assert.Equal(180, row.Bounds.Width, 0.01);
        Assert.Equal(1, row.Border.Bottom.Width, 0.01);
    }

    private static IEnumerable<float> Heights(Page page) =>
        page.Boxes.Where(box => box.Element == "td").Select(box => MathF.Round(box.Bounds.Height, 2));

    private static IEnumerable<float> Lefts(Page page) =>
        page.Boxes.Where(box => box.Element == "td").Select(box => MathF.Round(box.Bounds.X, 2));

    private static IEnumerable<float> Widths(Page page) =>
        page.Boxes.Where(box => box.Element == "td").Select(box => MathF.Round(box.Bounds.Width, 2));

    private static string Row(string? align)
    {
        string aligned = align is null ? "" : $";vertical-align:{align}";

        return $"<table><tr><td style='width:90pt'>a<br>b<br>c</td><td style='width:90pt{aligned}'>x</td></tr></table>";
    }

    private static string Rows(int count) =>
        string.Concat(Enumerable.Range(1, count).Select(number => $"<tr><td>{number}</td></tr>"));
}
