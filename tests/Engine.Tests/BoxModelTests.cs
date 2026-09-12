namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class BoxModelTests
{
    [Fact]
    public void Block_WithPaddingAndBorder_SetsItsTextInsideBoth()
    {
        Page page = Typeset.Html("<div style='margin:0;padding:10pt;border:2pt solid #000'>x</div>").Pages[0];
        RenderBox box = page.Boxes.Single(box => box.Element == "div");
        TextSpan span = Assert.Single(page.Spans);

        Assert.Equal(10, box.Bounds.X, 0.01);
        Assert.Equal(10, box.Bounds.Y, 0.01);
        Assert.Equal(180, box.Bounds.Width, 0.01);
        Assert.Equal(36, box.Bounds.Height, 0.01);
        Assert.Equal(2, box.Border.Top.Width, 0.01);
        Assert.Equal(22, span.X, 0.01);
        Assert.Equal(31.6, span.Baseline, 0.01);
    }

    [Fact]
    public void AdjacentMargins_CollapseToTheLargerOfTheTwo()
    {
        IReadOnlyList<TextSpan> spans = Typeset.Html("<p style='margin:10pt 0'>a</p><p style='margin:20pt 0'>b</p>").Pages[0].Spans;

        Assert.Equal(32, spans[1].Baseline - spans[0].Baseline, 0.01);
    }

    [Fact]
    public void AutoMargins_CenterABlockOfFixedWidth()
    {
        RenderBox box = Typeset.Html("<div style='width:100pt;margin:0 auto'>x</div>").Pages[0].Boxes.Single(box => box.Element == "div");

        Assert.Equal(50, box.Bounds.X, 0.01);
        Assert.Equal(100, box.Bounds.Width, 0.01);
    }

    [Fact]
    public void PercentageWidth_IsAShareOfTheContainingBlock()
    {
        RenderBox box = Typeset.Html("<div style='width:50%'>x</div>").Pages[0].Boxes.Single(box => box.Element == "div");

        Assert.Equal(90, box.Bounds.Width, 0.01);
    }

    [Fact]
    public void BorderBoxSizing_CountsPaddingInsideTheDeclaredWidth()
    {
        Page page = Typeset.Html("<div style='box-sizing:border-box;width:100pt;padding:0 10pt'>x</div>").Pages[0];

        Assert.Equal(100, page.Boxes.Single(box => box.Element == "div").Bounds.Width, 0.01);
        Assert.Equal(20, Assert.Single(page.Spans).X, 0.01);
    }

    [Fact]
    public void HiddenElement_TakesNoRoom()
    {
        TextSpan span = Assert.Single(Typeset.Html("<p style='margin:0;display:none'>a</p><p style='margin:0'>b</p>").Pages[0].Spans);

        Assert.Equal("b", span.Text);
        Assert.Equal(19.6, span.Baseline, 0.01);
    }

    [Fact]
    public void Background_IsPaintedOnTheBoxOfItsBlock()
    {
        RenderBox box = Typeset.Html("<div style='background:#ff0000'>x</div>").Pages[0].Boxes.Single(box => box.Element == "div");

        Assert.True(box.IsPainted);
        Assert.Equal(new Rgba(255, 0, 0), box.Background);
    }

    [Fact]
    public void BorderRadius_RoundsTheCornersOfABoxThatFitsOnOnePage()
    {
        RenderBox box = Typeset.Html("<div style='border-radius:6pt;background:#eeeeee'>x</div>").Pages[0].Boxes.Single(box => box.Element == "div");

        Assert.Equal(6, box.Radius, 0.01);
    }

    /// <summary>Rounding the cut edge of a split box would draw a corner where the content simply continues.</summary>
    [Fact]
    public void BorderRadius_OnABoxCrossingThePageEnd_IsDroppedOnBothFragments()
    {
        Composition composition = Typeset.Html($"<div style='border-radius:6pt;background:#eeeeee'>{Typeset.Numbered(20)}</div>");

        Assert.All(composition.Pages, page => Assert.Equal(0, page.Boxes.Single(box => box.Element == "div").Radius));
    }
}
