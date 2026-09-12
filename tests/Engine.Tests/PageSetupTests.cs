namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class PageSetupTests
{
    /// <summary>A5 is 148 by 210 mm with 15 mm margins: 419.53 by 595.28 points, 42.52 on each side.</summary>
    [Fact]
    public void A5_IsHalfAnA4SheetWithNarrowerMargins()
    {
        PageSetup sheet = PageSetup.A5;

        Assert.Equal(419.53, sheet.Width, 0.01);
        Assert.Equal(595.28, sheet.Height, 0.01);
        Assert.Equal(42.52, sheet.Margins.Left, 0.01);
        Assert.Equal(sheet.Margins.Left, sheet.Margins.Top, 0.01);
    }

    /// <summary>US Letter is 8.5 by 11 inches with one inch margins, and an inch is 72 points.</summary>
    [Fact]
    public void Letter_IsEightAndAHalfByElevenInchesWithInchMargins()
    {
        PageSetup sheet = PageSetup.Letter;

        Assert.Equal(612, sheet.Width, 0.01);
        Assert.Equal(792, sheet.Height, 0.01);
        Assert.Equal(72, sheet.Margins.Bottom, 0.01);
    }

    [Fact]
    public void Landscape_TurnsTheSheetAndKeepsItsMargins()
    {
        PageSetup sheet = PageSetup.A4.Landscape();

        Assert.Equal(PageSetup.A4.Height, sheet.Width, 0.01);
        Assert.Equal(PageSetup.A4.Width, sheet.Height, 0.01);
        Assert.Equal(PageSetup.A4.Margins, sheet.Margins);
    }

    /// <summary>Turning a sheet that already lies on its long side changes nothing, so a caller need not check first.</summary>
    [Fact]
    public void Landscape_OfALandscapeSheet_IsTheSameSheet()
    {
        PageSetup sheet = PageSetup.A4.Landscape();

        Assert.Equal(sheet, sheet.Landscape());
    }

    /// <summary>The page a landscape sheet makes is as wide as the paper is long: 841.89 by 595.28 points for A4.</summary>
    [Fact]
    public void Landscape_A4_MakesPagesAsWideAsThePaperIsLong()
    {
        Composition composition = new Typesetter(new FixedPitch()).Compose("<p>x</p>", PageSetup.A4.Landscape());
        Page page = Assert.Single(composition.Pages);

        Assert.Equal(841.89, page.Width, 0.01);
        Assert.Equal(595.28, page.Height, 0.01);
    }
}
