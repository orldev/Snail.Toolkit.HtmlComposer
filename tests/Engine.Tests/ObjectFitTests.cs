namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class ObjectFitTests
{
    private static Pictures Wide => new(("wide", 200, 100));

    /// <summary>A picture given both measurements is stretched to them, as CSS fills a frame by default.</summary>
    [Fact]
    public void PictureInAFrame_FillsItAndKeepsTheWholePicture()
    {
        ImageBox picture = Assert.Single(Framed(fit: null).Images);

        Assert.Equal(50, picture.Bounds.Width, 0.01);
        Assert.Equal(100, picture.Bounds.Height, 0.01);
        Assert.Null(picture.Source);
    }

    /// <summary>
    /// A frame twice as tall as it is wide takes a slice of a 200 by 100 picture 50 pixels across, from the
    /// middle: the frame is kept whole and 150 pixels of the picture are given up, 75 from either side.
    /// </summary>
    [Fact]
    public void PictureCoveringAFrame_KeepsTheFrameAndGivesUpWhatDoesNotFit()
    {
        ImageBox picture = Assert.Single(Framed("cover").Images);

        Assert.Equal(50, picture.Bounds.Width, 0.01);
        Assert.Equal(100, picture.Bounds.Height, 0.01);
        Assert.Equal(new Rect(75, 0, 50, 100), picture.Source);
    }

    /// <summary>The whole of a 2:1 picture inside a 50 by 100 frame is 50 by 25.</summary>
    [Fact]
    public void PictureContainedInAFrame_ShrinksToFitAndStaysWhole()
    {
        ImageBox picture = Assert.Single(Framed("contain").Images);

        Assert.Equal(50, picture.Bounds.Width, 0.01);
        Assert.Equal(25, picture.Bounds.Height, 0.01);
        Assert.Null(picture.Source);
    }

    /// <summary>Without both measurements there is no frame to fit into, so the picture keeps its own shape.</summary>
    [Fact]
    public void PictureWithOneMeasurement_IsNotCroppedHoweverItIsToldToFit()
    {
        string html = "<p style='margin:0'><img src='wide' style='width:50pt;object-fit:cover'></p>";

        ImageBox picture = Assert.Single(Typeset.Html(html, Wide).Pages[0].Images);

        Assert.Equal(25, picture.Bounds.Height, 0.01);
        Assert.Null(picture.Source);
    }

    private static Page Framed(string? fit)
    {
        string fitted = fit is null ? "" : $";object-fit:{fit}";

        return Typeset.Html($"<p style='margin:0'><img src='wide' style='width:50pt;height:100pt{fitted}'></p>", Wide).Pages[0];
    }
}
