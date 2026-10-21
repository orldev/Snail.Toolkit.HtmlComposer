namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

/// <summary>The structure a page carries beside its paint lists: blocks of text as paragraphs, tables as grids.</summary>
public sealed class StructureTests
{
    /// <summary>
    /// Thirty characters fit a line, so the paragraph wraps once, at the space it swallows; the frame starts below
    /// the 12 point margin a paragraph is given by default.
    /// </summary>
    [Fact]
    public void Paragraph_IsGivenBackAsOneFrameWhoseLinesJoinAtTheSpaceTheBreakTook()
    {
        Page page = Typeset.Structured("<p>aaaaaaaaaa bbbbbbbbbb cccccccccc dddd</p>").Pages[0];

        TextFrame frame = Assert.Single(page.Texts);
        TextParagraph paragraph = Assert.Single(frame.Paragraphs);

        Assert.Equal(2, paragraph.Lines.Count);
        Assert.True(paragraph.Lines[0].IsWrappedAtSpace);
        Assert.Equal(new Rect(10, 22, 180, 24), frame.Bounds);
        Assert.All(paragraph.Lines.SelectMany(line => line.Spans), span => Assert.Contains(span, page.Spans));
    }

    [Fact]
    public void ForcedBreak_StartsANewParagraphInTheSameFrame()
    {
        Page page = Typeset.Structured("<p style='text-align:right'>one<br>two</p>").Pages[0];

        TextFrame frame = Assert.Single(page.Texts);

        Assert.Equal(2, frame.Paragraphs.Count);
        Assert.All(frame.Paragraphs, paragraph => Assert.Equal(TextAlignment.Right, paragraph.Alignment));
    }

    [Fact]
    public void BlocksOneAfterAnother_AreFramesOfTheirOwn()
    {
        Page page = Typeset.Structured("<h1 style='font-size:12pt'>head</h1><p>body</p>").Pages[0];

        Assert.Equal(["head", "body"], [.. page.Texts.Select(frame => frame.Paragraphs[0].Lines[0].Spans[0].Text)]);
    }

    [Fact]
    public void LineHoldingAPicture_KeepsItsRunsPlacedInsteadOfAFrame()
    {
        Page page = Typeset.Structured("<p>a <img src='x.png' style='width:12pt;height:12pt'> b</p>", new Pictures(("x.png", 24, 12))).Pages[0];

        Assert.Empty(page.Texts);
    }

    [Fact]
    public void Table_IsGivenBackAsAGridWithColumnsFromItsCells()
    {
        Page page = Typeset.Structured(
            "<table><tr><td style='width:60pt'>a</td><td style='width:120pt'>b</td></tr>"
            + "<tr><td colspan='2'>wide</td></tr></table>").Pages[0];

        TableFrame table = Assert.Single(page.Tables);

        Assert.Equal([60, 120], [.. table.Columns]);
        Assert.Equal(new Rect(10, 10, 180, 24), table.Bounds);
        Assert.Equal((0, 2), (table.Rows[1].Cells[0].Column, table.Rows[1].Cells[0].Span));
        Assert.Equal("b", table.Rows[0].Cells[1].Paragraphs[0].Lines[0].Spans[0].Text);
        Assert.Empty(page.Texts);
    }

    /// <summary>
    /// The first row draws a light rule under itself and the second a heavy one above: the edge between them is
    /// one rule, the heavier, and both cells report it.
    /// </summary>
    [Fact]
    public void EdgeBetweenTwoRows_IsOneRuleBothCellsReport()
    {
        Page page = Typeset.Structured(
            "<table><tr><td style='border-bottom:0.5pt solid #ccc'>a</td></tr>"
            + "<tr><td style='border-top:2pt solid #000'>b</td></tr></table>").Pages[0];

        TableFrame table = Assert.Single(page.Tables);

        Assert.Equal(2, table.Rows[0].Cells[0].Bottom.Width);
        Assert.Equal(2, table.Rows[1].Cells[0].Top.Width);
        Assert.Equal(0, table.Rows[0].Cells[0].Top.Width);
    }

    [Fact]
    public void CellPadding_AndAlignment_AreGivenAsDeclared()
    {
        Page page = Typeset.Structured(
            "<table><tr><td style='height:60pt;padding:4pt 8pt 6pt 2pt;vertical-align:middle'>a</td></tr></table>").Pages[0];

        TableCell cell = Assert.Single(Assert.Single(page.Tables).Rows).Cells[0];

        Assert.Equal(CellAlignment.Middle, cell.Alignment);
        Assert.Equal(new Edges(4, 8, 6, 2), cell.Padding);
    }

    [Fact]
    public void CellHoldingOnlyAPicture_GivesThePictureAsTheCellsOwn()
    {
        Page page = Typeset.Structured(
            "<table><tr><td><img src='x.png' style='width:24pt;height:12pt'></td><td>t</td></tr></table>",
            new Pictures(("x.png", 24, 12))).Pages[0];

        TableRow row = Assert.Single(Assert.Single(page.Tables).Rows);

        Assert.Same(Assert.Single(page.Images), row.Cells[0].Picture);
        Assert.Null(row.Cells[1].Picture);
    }

    [Fact]
    public void TableWithACellSpanningRows_IsNotAGrid()
    {
        Page page = Typeset.Structured("<table><tr><td rowspan='2'>a</td><td>b</td></tr><tr><td>c</td></tr></table>").Pages[0];

        Assert.Empty(page.Tables);
    }

    [Fact]
    public void TableInsideACell_LeavesTheOuterTableUngridded()
    {
        Page page = Typeset.Structured("<table><tr><td><table><tr><td>in</td></tr></table></td></tr></table>").Pages[0];

        Assert.Single(page.Tables);
        Assert.Equal("in", page.Tables[0].Rows[0].Cells[0].Paragraphs[0].Lines[0].Spans[0].Text);
    }

    [Fact]
    public void RunningLine_IsToldApart_AndItsNumbersSayWhatTheyStandFor()
    {
        PageSetup setup = Typeset.Sheet with { Footer = "<p style='margin:0'>p. {page} of {pages}</p>" };
        Composition composition = new Typesetter(new FixedPitch()) { IsStructured = true }.Compose(Typeset.Numbered(20), setup);
        Page second = composition.Pages[1];

        Running running = Assert.IsType<Running>(second.Running);
        var fields = running.Spans.Where(span => span.Field != TextField.None).ToList();

        Assert.Equal([("2", TextField.PageNumber), ("2", TextField.PageCount)], [.. fields.Select(span => (span.Text, span.Field))]);
        Assert.All(running.Spans, span => Assert.Contains(span, second.Spans));
        Assert.Equal("p. 2 of 2", second.Lines[^1]);
        Assert.Single(running.Texts);
    }

    [Fact]
    public void PageWithoutRunningLines_HasNone()
    {
        Assert.Null(Typeset.Structured("text").Pages[0].Running);
    }

    /// <summary>Twenty rows of 12 points do not fit 180: the second page opens with the heading again, as a table of its own.</summary>
    [Fact]
    public void TableAcrossPages_IsAGridOnEachPage_HeadingFirst()
    {
        string rows = string.Concat(Enumerable.Range(1, 20).Select(number => $"<tr><td>r{number}</td></tr>"));
        Composition composition = Typeset.Structured($"<table><thead><tr><th>head</th></tr></thead><tbody>{rows}</tbody></table>");

        Assert.Equal(2, composition.Pages.Count);
        Assert.All(composition.Pages, page => Assert.True(Assert.Single(page.Tables).Rows[0].IsHeader));
    }
}
