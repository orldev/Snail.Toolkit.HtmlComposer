
namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class ImageTests
{
    private static readonly Pictures Photo = new(("photo.png", 100, 50));

    [Fact]
    public void Picture_WithoutADeclaredSize_TakesItsNaturalSize()
    {
        ImageBox image = Assert.Single(Typeset.Html("<img src='photo.png'>", Photo).Pages[0].Images);

        Assert.Equal(75, image.Bounds.Width, 0.01);
        Assert.Equal(37.5, image.Bounds.Height, 0.01);
    }

    [Fact]
    public void Picture_WithOnlyAWidthAttribute_KeepsItsProportions()
    {
        ImageBox image = Assert.Single(Typeset.Html("<img src='photo.png' width='200'>", Photo).Pages[0].Images);

        Assert.Equal(150, image.Bounds.Width, 0.01);
        Assert.Equal(75, image.Bounds.Height, 0.01);
    }

    [Fact]
    public void Picture_WithAPercentageWidth_TakesThatShareOfTheLine()
    {
        ImageBox image = Assert.Single(Typeset.Html("<img src='photo.png' style='width:50%'>", Photo).Pages[0].Images);

        Assert.Equal(90, image.Bounds.Width, 0.01);
        Assert.Equal(45, image.Bounds.Height, 0.01);
    }

    [Fact]
    public void Picture_InCenteredText_IsCentered()
    {
        ImageBox image = Assert.Single(Typeset.Html("<p style='margin:0;text-align:center'><img src='photo.png'></p>", Photo).Pages[0].Images);

        Assert.Equal(62.5, image.Bounds.X, 0.01);
    }

    [Fact]
    public void Picture_SitsOnTheBaselineOfItsLine()
    {
        Page page = Typeset.Html("<p style='margin:0'>x<img src='photo.png'></p>", Photo).Pages[0];
        ImageBox image = Assert.Single(page.Images);

        Assert.Equal(10, image.Bounds.Y, 0.01);
        Assert.Equal(Assert.Single(page.Spans).Baseline, image.Bounds.Bottom, 0.01);
    }

    [Fact]
    public void UnreadablePicture_LeavesANoteAndItsAlternativeText()
    {
        Composition composition = Typeset.Html("<p style='margin:0'><img src='missing.png' alt='Logo'></p>", Photo);

        Note note = Assert.Single(composition.Notes);

        Assert.Equal("Logo", Assert.Single(composition.Pages[0].Spans).Text);
        Assert.Equal(NoteKind.MissingPicture, note.Kind);
        Assert.Equal("missing.png", note.Subject);
    }

    [Fact]
    public void Typesetter_WithoutAnImageSource_SaysSoInTheNote()
    {
        Composition composition = Typeset.Html("<img src='photo.png'>");

        Assert.Contains("no image source", Assert.Single(composition.Notes).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Document_SetAsWritten_CarriesNoNotes() => Assert.Empty(Typeset.Html("<p>x</p><img src='photo.png'>", Photo).Notes);
}
