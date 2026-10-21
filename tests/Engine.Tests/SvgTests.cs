using System.Text;

namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class SvgTests
{
    [Fact]
    public void InlineSvg_WithOnlyAViewBox_TakesTheBoxAsItsSize()
    {
        ImageBox image = Assert.Single(Typeset.Html("<svg viewBox='0 0 200 100'><rect width='10' height='10'/></svg>").Pages[0].Images);

        Assert.True(image.Image.IsVector);
        Assert.Equal(150, image.Bounds.Width, 0.01);
        Assert.Equal(75, image.Bounds.Height, 0.01);
    }

    /// <summary>The words of a drawing belong to the drawing, and are not set again as lines of the page.</summary>
    [Fact]
    public void InlineSvg_WithALabel_SetsNoneOfItsWordsAsText()
    {
        Page page = Typeset.Html("<p style='margin:0'>Before<svg width='40' height='20'><text x='0' y='15'>Label</text></svg></p>").Pages[0];

        Assert.Single(page.Images);
        Assert.Equal("Before", Assert.Single(page.Spans).Text);
    }

    [Fact]
    public void InlineSvg_WithAPercentageWidth_KeepsItsProportions()
    {
        ImageBox image = Assert.Single(Typeset.Html("<svg viewBox='0 0 40 20' style='width:50%'></svg>").Pages[0].Images);

        Assert.Equal(90, image.Bounds.Width, 0.01);
        Assert.Equal(45, image.Bounds.Height, 0.01);
    }

    [Fact]
    public void InlineSvg_SetAsABlock_IsStillOnePicture()
    {
        Page page = Typeset.Html("<svg viewBox='0 0 40 20' style='display:block'><text>x</text></svg><p>After</p>").Pages[0];

        Assert.Single(page.Images);
        Assert.Equal("After", Assert.Single(page.Spans).Text);
    }

    [Fact]
    public void Read_AbsoluteWidthAndHeight_TakesThemInPixels()
    {
        Image? image = Svg.Read(Bytes("<svg width='2in' height='12pt'></svg>"));

        Assert.NotNull(image);
        Assert.Equal(192, image.Width);
        Assert.Equal(16, image.Height);
    }

    [Fact]
    public void Read_OnlyAWidth_TakesTheHeightFromTheViewBox()
    {
        Image? image = Svg.Read(Bytes("<svg width='100' viewBox='0 0 50 25'></svg>"));

        Assert.Equal((100, 50), (image?.Width, image?.Height));
    }

    [Fact]
    public void Read_NoSizeAtAll_TakesTheDefaultOfABrowser()
    {
        Image? image = Svg.Read(Bytes("<svg><circle r='4'/></svg>"));

        Assert.Equal((300, 150), (image?.Width, image?.Height));
    }

    [Fact]
    public void IsMarkup_AfterAByteOrderMarkAndADeclaration_RecognisesTheDrawing()
    {
        byte[] content = [0xEF, 0xBB, 0xBF, .. Bytes("<?xml version='1.0'?>\n<svg xmlns='http://www.w3.org/2000/svg'/>")];

        Assert.True(Svg.IsMarkup(content));
    }

    [Fact]
    public void IsMarkup_ThePngSignature_IsNotADrawing() =>
        Assert.False(Svg.IsMarkup([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]));

    [Fact]
    public void Read_MarkupWithoutADrawing_ReadsNothing() =>
        Assert.Null(Svg.Read(Bytes("<p>a paragraph about drawings, holding none</p>")));

    private static byte[] Bytes(string markup) => Encoding.UTF8.GetBytes(markup);
}
