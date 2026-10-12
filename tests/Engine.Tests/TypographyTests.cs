
namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class TypographyTests
{
    /// <summary>Three characters of 12pt text are 6 points each, and 2 points of tracking follows every one.</summary>
    [Fact]
    public void LetterSpacing_AddsItsWidthAfterEveryGlyph()
    {
        TextSpan span = Assert.Single(Typeset.Html("<p style='margin:0;letter-spacing:2pt'>abc</p>").Pages[0].Spans);

        Assert.Equal(24, span.Width, 0.01);
        Assert.Equal(2, span.Font.Tracking, 0.01);
    }

    /// <summary>An em of tracking is an em of the element's own size: 0.1em of 20pt text is 2 points.</summary>
    [Fact]
    public void LetterSpacingInEm_IsAShareOfTheElementsOwnFontSize()
    {
        TextSpan span = Assert.Single(Typeset.Html("<p style='margin:0;font-size:20pt;letter-spacing:0.1em'>ab</p>").Pages[0].Spans);

        Assert.Equal(24, span.Width, 0.01);
    }

    [Fact]
    public void LetterSpacing_IsInheritedByNestedText()
    {
        TextSpan span = Assert.Single(Typeset.Html("<div style='margin:0;letter-spacing:3pt'><p style='margin:0'>ab</p></div>").Pages[0].Spans);

        Assert.Equal(18, span.Width, 0.01);
    }

    [Fact]
    public void LetterSpacingNormal_DropsTheTrackingItInherited()
    {
        TextSpan span = Assert.Single(Typeset.Html("<div style='margin:0;letter-spacing:3pt'><p style='margin:0;letter-spacing:normal'>ab</p></div>").Pages[0].Spans);

        Assert.Equal(12, span.Width, 0.01);
        Assert.Equal(0, span.Font.Tracking, 0.01);
    }

    /// <summary>Twenty-four characters fit the 180 point line at 6 points each, but not at 8.</summary>
    /// <remarks>
    /// The line breaker has to see the tracking, not only the renderer: a run measured without it would be
    /// broken as if it were a quarter shorter, and every line would overrun the column it was set in.
    /// </remarks>
    [Fact]
    public void LetterSpacing_BreaksTheLineEarlierThanTheUntrackedText()
    {
        const string text = "aaaa bbbb cccc dddd eeee";

        Page loose = Typeset.Html($"<p style='margin:0;letter-spacing:2pt'>{text}</p>").Pages[0];

        Assert.Equal([text], Typeset.Html($"<p style='margin:0'>{text}</p>").Pages[0].Lines);
        Assert.Equal(["aaaa bbbb cccc dddd", "eeee"], loose.Lines);
    }

    /// <summary>A 16pt picture on the baseline makes the line 16pt tall and puts the baseline at its foot.</summary>
    [Fact]
    public void Picture_SitsOnTheBaselineUnlessToldOtherwise()
    {
        Page page = Typeset.Html(Header(align: null), Eye).Pages[0];

        Assert.Equal(10, Assert.Single(page.Images).Bounds.Y, 0.01);
        Assert.Equal(26, Assert.Single(page.Spans).Baseline, 0.01);
    }

    /// <summary>
    /// Middle centres the picture on the middle of the lower case, half an x-height above the baseline: of the
    /// 16 points, 11 sit above the baseline and 5 below, so the baseline rises from 26 to 21.
    /// </summary>
    /// <remarks>
    /// This is what an icon beside small text needs. Left on the baseline, a picture taller than the text drags
    /// the baseline down to its foot, and the text of the line is pressed against the bottom of the picture.
    /// </remarks>
    [Fact]
    public void PictureAlignedMiddle_CentresItselfAgainstTheTextInsteadOfStandingOnIt()
    {
        Page page = Typeset.Html(Header("middle"), Eye).Pages[0];

        Assert.Equal(10, Assert.Single(page.Images).Bounds.Y, 0.01);
        Assert.Equal(21, Assert.Single(page.Spans).Baseline, 0.01);
    }

    /// <summary>Four points before and eight after a 16 point picture: it is drawn at 14 and the text starts at 38.</summary>
    /// <remarks>
    /// The margins advance the line without widening the picture, which is what holds a running head off its
    /// icon. Writing a space between them instead would give whatever the face makes a space, at the size of
    /// the text rather than the size the design asks for.
    /// </remarks>
    [Fact]
    public void PictureMargins_HoldTheTextOffThePictureWithoutStretchingIt()
    {
        const string html = "<p style='margin:0'><img src='eye' style='width:16pt;height:16pt;margin-left:4pt;margin-right:8pt'>x</p>";

        Page page = Typeset.Html(html, Eye).Pages[0];
        ImageBox picture = Assert.Single(page.Images);

        Assert.Equal(14, picture.Bounds.X, 0.01);
        Assert.Equal(16, picture.Bounds.Width, 0.01);
        Assert.Equal(38, Assert.Single(page.Spans).X, 0.01);
    }

    [Fact]
    public void TextTransformUppercase_RaisesTheTextThatIsSet()
    {
        TextSpan span = Assert.Single(Typeset.Html("<p style='margin:0;text-transform:uppercase'>dossier</p>").Pages[0].Spans);

        Assert.Equal("DOSSIER", span.Text);
    }

    /// <summary>Cyrillic has a case of its own, and the documents this library sets are as often Cyrillic as Latin.</summary>
    [Fact]
    public void TextTransformUppercase_RaisesCyrillicLettersToo()
    {
        TextSpan span = Assert.Single(Typeset.Html("<p style='margin:0;text-transform:uppercase'>досье</p>").Pages[0].Spans);

        Assert.Equal("ДОСЬЕ", span.Text);
    }

    [Fact]
    public void TextTransformCapitalize_RaisesOnlyTheFirstLetterOfEachWord()
    {
        TextSpan span = Assert.Single(Typeset.Html("<p style='margin:0;text-transform:capitalize'>what was found</p>").Pages[0].Spans);

        Assert.Equal("What Was Found", span.Text);
    }

    [Fact]
    public void TextTransform_IsInheritedByNestedText()
    {
        Page page = Typeset.Html("<div style='margin:0;text-transform:uppercase'><p style='margin:0'>p. 4</p></div>").Pages[0];

        Assert.Equal(["P. 4"], page.Lines);
    }

    private static Pictures Eye => new(("eye", 100, 100));

    private static string Header(string? align)
    {
        string aligned = align is null ? "" : $";vertical-align:{align}";

        return $"<p style='margin:0'><img src='eye' style='width:16pt;height:16pt{aligned}'>x</p>";
    }
}
