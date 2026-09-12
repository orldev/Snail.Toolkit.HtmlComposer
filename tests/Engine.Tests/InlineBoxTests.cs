namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class InlineBoxTests
{
    /// <summary>«a» is six points wide, so the span starts at 16 and covers its own two characters.</summary>
    [Fact]
    public void SpanWithABackground_IsPaintedBehindItsOwnRunOnly()
    {
        Page page = Typeset.Html("<p style='margin:0'>a<span style='background:#ff0000'>bc</span></p>").Pages[0];

        RenderBox box = Assert.Single(page.Boxes, box => box.Kind == BoxKind.Inline);

        Assert.Equal(16, box.Bounds.X, 0.01);
        Assert.Equal(12, box.Bounds.Width, 0.01);
        Assert.Equal(new Rgba(255, 0, 0), box.Background);
    }

    /// <summary>A 12pt line is 12 points tall: 9.6 of ascent and 2.4 of descent, and the box takes both.</summary>
    [Fact]
    public void InlineBox_IsAsTallAsTheRunsOwnLine()
    {
        Page page = Typeset.Html("<p style='margin:0'><span style='background:#eeeeee'>x</span></p>").Pages[0];

        RenderBox box = Assert.Single(page.Boxes, box => box.Kind == BoxKind.Inline);

        Assert.Equal(10, box.Bounds.Y, 0.01);
        Assert.Equal(12, box.Bounds.Height, 0.01);
    }

    /// <summary>A small span inside a large one is painted around its own text, not around the line.</summary>
    /// <remarks>
    /// The line is as tall as its largest text, but an inline box belongs to the run that asked for it. Taking
    /// the line's height would draw a small highlight as a tall block reaching past the letters beside it.
    /// </remarks>
    [Fact]
    public void SmallSpanInATallLine_IsPaintedAroundItsOwnText()
    {
        string html = "<p style='margin:0;font-size:24pt'>a<span style='font-size:12pt;background:#eeeeee'>b</span></p>";

        RenderBox box = Assert.Single(Typeset.Html(html).Pages[0].Boxes, box => box.Kind == BoxKind.Inline);

        Assert.Equal(12, box.Bounds.Height, 0.01);
    }

    [Fact]
    public void SpanWithABorder_IsPaintedAroundItsRun()
    {
        Page page = Typeset.Html("<p style='margin:0'><span style='border:1pt solid #000000'>x</span></p>").Pages[0];

        RenderBox box = Assert.Single(page.Boxes, box => box.Kind == BoxKind.Inline);

        Assert.Equal(1, box.Border.Top.Width, 0.01);
        Assert.Equal(6, box.Bounds.Width, 0.01);
    }

    /// <summary>A 40 by 20 picture in a 1pt border is framed at 42 by 22, the rule standing outside it.</summary>
    /// <remarks>
    /// Sharing the picture's own rectangle would hide the rule: boxes are painted before pictures, so the
    /// picture would cover it. A border belongs outside the thing it frames in any case.
    /// </remarks>
    [Fact]
    public void PictureWithABorder_IsFramedJustOutsideItsOwnEdges()
    {
        string html = "<p style='margin:0'><img src='wide' style='width:40pt;height:20pt;border:1pt solid #000000'></p>";

        Page page = Typeset.Html(html, new Pictures(("wide", 200, 100))).Pages[0];
        RenderBox box = Assert.Single(page.Boxes, box => box.Kind == BoxKind.Inline);
        ImageBox picture = Assert.Single(page.Images);

        Assert.Equal(picture.Bounds.X - 1, box.Bounds.X, 0.01);
        Assert.Equal(42, box.Bounds.Width, 0.01);
        Assert.Equal(22, box.Bounds.Height, 0.01);
        Assert.Equal(1, box.Border.Top.Width, 0.01);
    }

    /// <summary>A block paints its own box, and its text must not paint it a second time.</summary>
    /// <remarks>
    /// A text node has no style of its own and carries its parent's. Left alone, every letter of a bordered
    /// cell came out in a little frame of its own — which is how this was found, on a rendered grid.
    /// </remarks>
    [Fact]
    public void TextInsideABorderedBlock_IsNotFramedItself()
    {
        Page page = Typeset.Html("<div style='margin:0;border:1pt solid #000000;background:#eeeeee'>text</div>").Pages[0];

        Assert.DoesNotContain(page.Boxes, box => box.Kind == BoxKind.Inline);
        Assert.Equal(1, Assert.Single(page.Boxes, box => box.Element == "div").Border.Top.Width, 0.01);
    }

    /// <summary>A cell of a table is a block too, and a grid must not frame the letters standing in it.</summary>
    [Fact]
    public void TextInsideABorderedCell_IsNotFramedItself()
    {
        string html = "<table><tr><td style='width:90pt;border:1pt solid #000000'>a</td></tr></table>";

        Assert.DoesNotContain(Typeset.Html(html).Pages[0].Boxes, box => box.Kind == BoxKind.Inline);
    }

    [Fact]
    public void TextWithoutABackgroundOrBorder_PaintsNoInlineBox()
    {
        Page page = Typeset.Html("<p style='margin:0'>plain text</p>").Pages[0];

        Assert.DoesNotContain(page.Boxes, box => box.Kind == BoxKind.Inline);
    }
}
