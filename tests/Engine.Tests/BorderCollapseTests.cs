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

    private static IReadOnlyList<RenderBox> Cells(string style) =>
        [.. Typeset.Html(string.Format(Grid, style)).Pages[0].Boxes.Where(box => box.Element == "td")];
}
