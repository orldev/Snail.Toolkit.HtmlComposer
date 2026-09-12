using System.Globalization;

namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class StylingTests
{
    [Fact]
    public void Heading_ByDefault_IsTwiceTheBodySizeAndBold()
    {
        Font font = SpanOf("<h1>Title</h1>", "Title").Font;

        Assert.Equal(24, font.Size, 0.01);
        Assert.Equal(700, font.Weight);
    }

    [Fact]
    public void EmUnits_ScaleWithTheParentFont()
    {
        TextSpan span = SpanOf("<div style='font-size:10pt'><span style='font-size:2em'>x</span></div>", "x");

        Assert.Equal(20, span.Font.Size, 0.01);
    }

    [Fact]
    public void RemUnits_ScaleWithTheRootFont()
    {
        TextSpan span = SpanOf("<html style='font-size:10pt'><body><p style='font-size:2rem'>x</p></body></html>", "x");

        Assert.Equal(20, span.Font.Size, 0.01);
    }

    [Fact]
    public void AuthorStylesheet_OverridesTheDefaults()
    {
        TextSpan span = SpanOf("<style>h1 { font-size: 10pt }</style><h1>x</h1>", "x");

        Assert.Equal(10, span.Font.Size, 0.01);
    }

    [Fact]
    public void ClassSelector_OutweighsElementSelector_WhateverTheirOrder()
    {
        TextSpan span = SpanOf("<style>.big { font-size: 20pt } p { font-size: 10pt }</style><p class='big'>x</p>", "x");

        Assert.Equal(20, span.Font.Size, 0.01);
    }

    [Fact]
    public void ImportantDeclaration_OutweighsInlineStyle()
    {
        TextSpan span = SpanOf("<style>p { color: #00ff00 !important }</style><p style='color:#ff0000'>x</p>", "x");

        Assert.Equal(new Rgba(0, 255, 0), span.Color);
    }

    [Fact]
    public void Color_IsInheritedByNestedText()
    {
        TextSpan span = SpanOf("<div style='color:#ff0000'><p><span>x</span></p></div>", "x");

        Assert.Equal(new Rgba(255, 0, 0), span.Color);
    }

    [Fact]
    public void ScreenOnlyRules_DoNotApplyToPaper()
    {
        TextSpan span = SpanOf("<style>@media screen { p { color: #ff0000 } } @media print { p { font-size: 20pt } }</style><p>x</p>", "x");

        Assert.Equal(Rgba.Black, span.Color);
        Assert.Equal(20, span.Font.Size, 0.01);
    }

    [Fact]
    public void BoldElement_InRegularText_IsSetBold()
    {
        TextSpan span = SpanOf("<p>regular <b>bold</b></p>", "bold");

        Assert.Equal(700, span.Font.Weight);
    }

    [Fact]
    public void FontFamily_IsPassedToTheRendererAsWritten()
    {
        TextSpan span = SpanOf("<p style=\"font-family: 'PT Sans', Arial, sans-serif\">x</p>", "x");

        string[] expected = ["PT Sans", "Arial", "sans-serif"];
        Assert.Equal(expected, span.Font.Families);
    }

    [Fact]
    public void UnitlessLineHeight_SpacesLinesByThatShareOfTheFontSize()
    {
        IReadOnlyList<TextSpan> spans = Typeset.Html("<p style='margin:0;line-height:2'>one<br>two</p>").Pages[0].Spans;

        Assert.Equal(25.6, spans[0].Baseline, 0.01);
        Assert.Equal(24, spans[1].Baseline - spans[0].Baseline, 0.01);
    }

    [Fact]
    public void Underline_IsCarriedIntoNestedText()
    {
        IReadOnlyList<TextSpan> spans = Typeset.Html("<p style='text-decoration:underline'>plain <span>nested</span></p>").Pages[0].Spans;

        Assert.All(spans, span => Assert.Equal(TextDecoration.Underline, span.Decoration));
    }

    [Fact]
    public void Lengths_UnderACultureWithADecimalComma_AreReadTheSame()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        try
        {
            Assert.Equal(18, SpanOf("<p style='font-size:1.5em'>x</p>", "x").Font.Size, 0.01);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static TextSpan SpanOf(string html, string text) =>
        Typeset.Html(html).Pages[0].Spans.Single(span => span.Text == text);
}
