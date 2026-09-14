namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

/// <summary>How a box cut by a page boundary is drawn: as one box opened and closed, or as whole fragments.</summary>
public sealed class DecorationBreakTests
{
    private static string Html(string rule) =>
        $"<div style='padding:20pt 0;{rule}'>{Typeset.Numbered(20)}</div>";

    /// <summary>The default: the cut sides carry neither padding nor border, so the box reads as one.</summary>
    [Fact]
    public void SlicedBox_OpensTheContinuationWithoutItsPadding()
    {
        Composition composition = Typeset.Html(Html(""));

        Assert.True(composition.Pages.Count > 1);
        Assert.Equal(10, composition.Pages[1].Spans[0].Baseline - 9.6f, 0.01);
    }

    /// <summary>
    /// With <c>clone</c> every fragment is whole, which is what a section carried over needs when the air
    /// above its content belongs on every page it reaches rather than only the one it began on.
    /// </summary>
    [Fact]
    public void ClonedBox_OpensTheContinuationWithItsPadding()
    {
        Composition composition = Typeset.Html(Html("box-decoration-break:clone"));

        Assert.True(composition.Pages.Count > 1);
        Assert.Equal(30, composition.Pages[1].Spans[0].Baseline - 9.6f, 0.01);
    }

    /// <summary>A cloned fragment draws its own border where a sliced one leaves the cut open.</summary>
    [Fact]
    public void ClonedBox_DrawsItsBorderOnEveryFragment()
    {
        Composition composition = Typeset.Html(
            $"<div style='border:1pt solid #000000;box-decoration-break:clone'>{Typeset.Numbered(20)}</div>");

        RenderBox box = composition.Pages[1].Boxes.First(b => b.Element == "div");

        Assert.False(box.IsSplitAbove);
        Assert.Equal(1, box.Border.Top.Width, 0.01);
    }
}
