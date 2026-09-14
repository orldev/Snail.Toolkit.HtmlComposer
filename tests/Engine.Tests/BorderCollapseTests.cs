namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class BorderCollapseTests
{
    private const string Grid = "<table style='{0}'>"
        + "<tr><td style='width:90pt;border:1pt solid #000000'>a</td><td style='width:90pt;border:1pt solid #000000'>b</td></tr>"
        + "<tr><td style='border:1pt solid #000000'>c</td><td style='border:1pt solid #000000'>d</td></tr>"
        + "</table>";

    /// <summary>Between two cells each drew its own rule, so a one point grid came out two points thick.</summary>
    [Fact]
    public void CollapsedTable_LeavesOneRuleWhereTwoCellsMeet()
    {
        IReadOnlyList<RenderBox> cells = Cells("border-collapse:collapse");

        Assert.Equal(0, cells[0].Border.Right.Width, 0.01);
        Assert.Equal(0, cells[0].Border.Bottom.Width, 0.01);
        Assert.Equal(1, cells[0].Border.Left.Width, 0.01);
        Assert.Equal(1, cells[0].Border.Top.Width, 0.01);
    }

    [Fact]
    public void CollapsedTable_KeepsTheRulesAtItsOwnEdges()
    {
        IReadOnlyList<RenderBox> cells = Cells("border-collapse:collapse");

        Assert.Equal(1, cells[1].Border.Right.Width, 0.01);
        Assert.Equal(1, cells[3].Border.Bottom.Width, 0.01);
    }

    [Fact]
    public void SeparateTable_LeavesEveryCellItsOwnRules()
    {
        IReadOnlyList<RenderBox> cells = Cells("border-collapse:separate");

        Assert.All(cells, cell => Assert.Equal(1, cell.Border.Right.Width, 0.01));
        Assert.All(cells, cell => Assert.Equal(1, cell.Border.Bottom.Width, 0.01));
    }

    /// <summary>A table says nothing about collapsing by default, and CSS keeps the cells apart.</summary>
    [Fact]
    public void TableWithoutTheProperty_LeavesEveryCellItsOwnRules() => Assert.All(Cells(""), cell => Assert.Equal(1, cell.Border.Right.Width, 0.01));

    /// <summary>
    /// The rules between rows used to vanish: a cell surrendered its bottom to whatever stood beneath it,
    /// and cells that declare a bottom and nothing else have no neighbour drawing a top to replace it.
    /// </summary>
    [Fact]
    public void CollapsedTable_KeepsARuleTheNeighbourBelowDoesNotDraw()
    {
        IReadOnlyList<RenderBox> cells = Ruled("border-bottom:1pt solid #000000");

        Assert.Equal(3, cells.Count);
        Assert.All(cells, cell => Assert.Equal(1, cell.Border.Bottom.Width, 0.01));
    }

    /// <summary>The same holds across, where a cell declares only the rule on its right.</summary>
    [Fact]
    public void CollapsedTable_KeepsARuleTheNeighbourBesideDoesNotDraw()
    {
        const string html = "<table style='border-collapse:collapse'><tr>"
            + "<td style='width:60pt;border-right:1pt solid #000000'>a</td>"
            + "<td style='width:60pt;border-right:1pt solid #000000'>b</td>"
            + "<td style='width:60pt;border-right:1pt solid #000000'>c</td>"
            + "</tr></table>";

        IReadOnlyList<RenderBox> cells = [.. Typeset.Html(html).Pages[0].Boxes.Where(box => box.Element == "td")];

        Assert.Equal(3, cells.Count);
        Assert.All(cells, cell => Assert.Equal(1, cell.Border.Right.Width, 0.01));
    }

    /// <summary>Where both sides of an edge are declared, the thicker is the one that survives.</summary>
    [Fact]
    public void CollapsedTable_KeepsTheThickerOfTwoRules()
    {
        const string html = "<table style='border-collapse:collapse'>"
            + "<tr><td style='border-bottom:2pt solid #000000'>a</td></tr>"
            + "<tr><td style='border-top:0.5pt solid #000000'>b</td></tr>"
            + "</table>";

        IReadOnlyList<RenderBox> cells = [.. Typeset.Html(html).Pages[0].Boxes.Where(box => box.Element == "td")];

        Assert.Equal(2, cells[0].Border.Bottom.Width, 0.01);
    }

    /// <summary>A cell spanning columns meets every cell beneath it, and one of them drawing is enough.</summary>
    [Fact]
    public void CollapsedTable_WeighsASpanningCellAgainstEveryCellBeneathIt()
    {
        const string html = "<table style='border-collapse:collapse'>"
            + "<tr><td colspan='2' style='border-bottom:1pt solid #000000'>wide</td></tr>"
            + "<tr><td style='width:60pt;border-top:1pt solid #000000'>a</td><td style='width:60pt'>b</td></tr>"
            + "</table>";

        IReadOnlyList<RenderBox> cells = [.. Typeset.Html(html).Pages[0].Boxes.Where(box => box.Element == "td")];

        Assert.Equal(0, cells[0].Border.Bottom.Width, 0.01);
    }

    private static IReadOnlyList<RenderBox> Cells(string style) =>
        [.. Typeset.Html(string.Format(Grid, style)).Pages[0].Boxes.Where(box => box.Element == "td")];

    private static IReadOnlyList<RenderBox> Ruled(string cell)
    {
        string html = $"<table style='border-collapse:collapse'>"
            + $"<tr><td style='{cell}'>a</td></tr>"
            + $"<tr><td style='{cell}'>b</td></tr>"
            + $"<tr><td style='{cell}'>c</td></tr>"
            + "</table>";

        return [.. Typeset.Html(html).Pages[0].Boxes.Where(box => box.Element == "td")];
    }
}
