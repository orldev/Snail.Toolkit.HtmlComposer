
namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class PaginationTests
{
    [Fact]
    public void Document_TallerThanOnePage_ContinuesOnTheNext()
    {
        Composition composition = Typeset.Html($"<p style='margin:0'>{Typeset.Numbered(20)}</p>");

        string[] rest = ["line16", "line17", "line18", "line19", "line20"];
        Assert.Equal(2, composition.Pages.Count);
        Assert.Equal(15, composition.Pages[0].Lines.Count);
        Assert.Equal(rest, composition.Pages[1].Lines);
    }

    /// <summary>The paragraph is cut between lines, and each page gets its own box for the part it holds.</summary>
    [Fact]
    public void Paragraph_CrossingThePageEnd_BecomesOneBoxOnEachPage()
    {
        Composition composition = Typeset.Html($"<p style='margin:0;background:#eeeeee'>{Typeset.Numbered(20)}</p>");
        RenderBox first = composition.Pages[0].Boxes.Single(box => box.Element == "p");
        RenderBox second = composition.Pages[1].Boxes.Single(box => box.Element == "p");

        Assert.False(first.IsSplitAbove);
        Assert.True(first.IsSplitBelow);
        Assert.Equal(180, first.Bounds.Height, 0.01);
        Assert.True(second.IsSplitAbove);
        Assert.False(second.IsSplitBelow);
        Assert.Equal(10, second.Bounds.Y, 0.01);
        Assert.Equal(60, second.Bounds.Height, 0.01);
    }

    /// <summary>Fourteen lines would fit, but the fifteenth would stand alone overleaf, so two go over together.</summary>
    [Fact]
    public void LastLine_IsNeverLeftAloneAtTheTopOfTheNextPage()
    {
        Composition composition = Typeset.Html($"<div style='height:12pt'></div><p style='margin:0'>{Typeset.Numbered(15)}</p>");

        string[] rest = ["line14", "line15"];
        Assert.Equal(13, composition.Pages[0].Lines.Count);
        Assert.Equal(rest, composition.Pages[1].Lines);
    }

    [Fact]
    public void Paragraph_WithRoomForOneLineOnly_MovesWholeToTheNextPage()
    {
        Composition composition = Typeset.Html($"<div style='height:168pt'></div><p style='margin:0'>{Typeset.Numbered(3)}</p>");

        string[] moved = ["line1", "line2", "line3"];
        Assert.Empty(composition.Pages[0].Spans);
        Assert.Equal(moved, composition.Pages[1].Lines);
    }

    [Fact]
    public void BreakBeforePage_StartsTheBlockOnANewPage()
    {
        Composition composition = Typeset.Html("<p style='margin:0'>a</p><p style='margin:0;break-before:page'>b</p>");

        Assert.Equal(2, composition.Pages.Count);
        Assert.Equal("b", Assert.Single(composition.Pages[1].Lines));
    }

    [Fact]
    public void LegacyPageBreakBefore_StartsTheBlockOnANewPage()
    {
        Composition composition = Typeset.Html("<p style='margin:0'>a</p><p style='margin:0;page-break-before:always'>b</p>");

        Assert.Equal(2, composition.Pages.Count);
    }

    [Fact]
    public void BreakAfterPage_OnTheLastBlock_AddsNoBlankPage()
    {
        Composition composition = Typeset.Html("<p style='margin:0;break-after:page'>a</p>");

        Assert.Single(composition.Pages);
    }

    [Fact]
    public void BreakInsideAvoid_MovesTheWholeBlockRatherThanSplitIt()
    {
        Composition composition = Typeset.Html($"<div style='height:150pt'></div><div style='break-inside:avoid'>{Typeset.Numbered(5)}</div>");

        Assert.Empty(composition.Pages[0].Spans);
        Assert.Equal(5, composition.Pages[1].Lines.Count);
    }

    /// <summary>
    /// Five of the eight lines would fit, but the paragraph asks to keep more together than it has, so it
    /// moves whole.
    /// </summary>
    /// <remarks>
    /// The count is a number the markup writes, and narrowing a float past <c>int.MaxValue</c> is undefined
    /// rather than saturating: this arrived as <c>int.MinValue</c>, which reads as "no number of orphans is
    /// too few" — the opposite of what was asked — and the paragraph was split after all.
    /// </remarks>
    [Fact]
    public void ParagraphAskingForMoreOrphansThanItHasLines_MovesWholeRatherThanSplitting()
    {
        Composition composition = Typeset.Html(
            $"<div style='height:120pt'></div><p style='margin:0;orphans:99999999999'>{Typeset.Numbered(8)}</p>");

        Assert.Empty(composition.Pages[0].Spans);
        Assert.Equal(8, composition.Pages[1].Lines.Count);
    }

    /// <summary>
    /// A block that refuses to be broken and is taller than a whole page is broken anyway, because a page that
    /// took nothing would be followed by another just like it, for ever.
    /// </summary>
    [Fact]
    public void BlockAvoidingBreaks_TallerThanAWholePage_IsSetRatherThanLoopingForever()
    {
        Composition composition = Typeset.Html($"<div style='break-inside:avoid'>{Typeset.Numbered(20)}</div>");

        Assert.Equal(2, composition.Pages.Count);
        Assert.Equal(15, composition.Pages[0].Lines.Count);
    }

    /// <summary>The heading fits at the bottom of the page, but its paragraph does not, so both move.</summary>
    [Fact]
    public void Heading_AtTheBottomOfAPage_MovesWithTheParagraphBelowIt()
    {
        Composition composition = Typeset.Html(
            $"<div style='height:150pt'></div><h2 style='margin:0;font-size:12pt'>Heading</h2><p style='margin:0'>{Typeset.Numbered(3)}</p>");

        Assert.Empty(composition.Pages[0].Spans);
        Assert.Equal("Heading", composition.Pages[1].Lines[0]);
    }

    [Fact]
    public void EmptyDocument_StillHasOnePage()
    {
        Page page = Assert.Single(Typeset.Html("").Pages);

        Assert.Empty(page.Spans);
    }

    [Fact]
    public void Picture_TallerThanTheRoomLeft_MovesToTheNextPage()
    {
        Composition composition = Typeset.Html("<div style='height:150pt'></div><img src='photo.png'>", new Pictures(("photo.png", 100, 100)));

        Assert.Empty(composition.Pages[0].Images);
        Assert.Equal(10, Assert.Single(composition.Pages[1].Images).Bounds.Y, 0.01);
    }

    [Fact]
    public void Picture_TallerThanAWholePage_ShrinksToFitIt()
    {
        Composition composition = Typeset.Html("<img src='poster.png'>", new Pictures(("poster.png", 1000, 2000)));
        ImageBox image = Assert.Single(Assert.Single(composition.Pages).Images);

        Assert.Equal(90, image.Bounds.Width, 0.01);
        Assert.Equal(180, image.Bounds.Height, 0.01);
    }
}
