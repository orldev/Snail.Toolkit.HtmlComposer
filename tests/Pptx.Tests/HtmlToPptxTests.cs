using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Validation;
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Snail.Toolkit.HtmlComposer.Pptx.Tests;

public sealed class HtmlToPptxTests : IDisposable
{
    private const string Pangram = "Съешь же ещё этих мягких французских булок, да выпей чаю.";

    private readonly FontLibrary _fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));

    public void Dispose() => _fonts.Dispose();

    [Fact]
    public void Convert_Document_WritesOneSlidePerPage()
    {
        string lines = string.Join("<br>", Enumerable.Range(1, 120).Select(number => $"Line{number}"));
        using var output = new MemoryStream();

        Composition composition = new HtmlToPptx(_fonts).Write($"<p>{lines}</p>", output);

        using var deck = PresentationDocument.Open(output, false);

        Assert.True(composition.Pages.Count > 1);
        Assert.Equal(composition.Pages.Count, deck.PresentationPart!.SlideParts.Count());
    }

    /// <summary>A slide is the sheet: 595 by 842 points of A4 are 7 556 500 by 10 693 400 EMU.</summary>
    [Fact]
    public void Convert_A4Document_SizesTheSlideLikeTheSheet()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert("<p>x</p>"));

        SlideSize size = deck.PresentationPart!.Presentation!.SlideSize!;

        Assert.Equal((int)Math.Round(PageSetup.A4.Width * 12700), size.Cx!.Value);
        Assert.Equal((int)Math.Round(PageSetup.A4.Height * 12700), size.Cy!.Value);
    }

    [Fact]
    public void Convert_CyrillicText_KeepsItReadableInTheSlide()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert($"<p>{Pangram}</p>"));

        string text = string.Concat(Texts(deck));

        Assert.Contains("французских", text, StringComparison.Ordinal);
    }

    /// <summary>Every run is placed where the engine set it, so the deck carries the layout, not a guess at it.</summary>
    [Fact]
    public void Convert_Text_PlacesTheRunWhereTheEngineSetIt()
    {
        const string html = "<p>Line width</p>";
        var converter = new HtmlToPptx(_fonts);
        TextSpan span = Assert.Single(converter.Typeset(html).Pages[0].Spans);

        using PresentationDocument deck = Open(converter.Convert(html));
        A.Offset offset = Assert.Single(Shapes(deck), shape => shape.TextBody?.InnerText.Length > 0)
            .ShapeProperties!.Transform2D!.Offset!;

        Assert.Equal((long)Math.Round(span.X * 12700), offset.X!.Value);
    }

    [Fact]
    public void Convert_BoldText_WritesItBold()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert("<p><b>Bold</b></p>"));

        A.Run run = Assert.Single(Runs(deck));

        Assert.True(run.RunProperties!.Bold!.Value);
    }

    [Fact]
    public void Convert_ColouredBox_WritesItAsAFilledShape()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert("<div style='background:#d52321;width:60pt;height:20pt'></div>"));

        IEnumerable<string?> fills = Shapes(deck)
            .Select(shape => shape.ShapeProperties?.GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value)
            .Where(value => value is not null);

        Assert.Contains("D52321", fills);
    }

    [Fact]
    public void Convert_Picture_EmbedsItInTheSlide()
    {
        string source = $"data:image/png;base64,{System.Convert.ToBase64String(RedSquare())}";

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert($"<img src='{source}' width='40'>"));

        Assert.Single(deck.PresentationPart!.SlideParts.Single().ImageParts);
    }

    /// <summary>A logo in a running head is on every slide and must still be in the file only once.</summary>
    /// <remarks>
    /// Each page reads the picture again, so the same bytes arrive as a different object every time. Stored
    /// per slide, a logo would weigh as much as the deck has pages — the very reason the PDF press keeps one
    /// decoded copy per document. A running head now goes onto the slide layout, so the one copy is the
    /// layout's and the slides carry none.
    /// </remarks>
    [Fact]
    public void Convert_PictureOnEverySlide_StoresItOnce()
    {
        string source = $"data:image/png;base64,{System.Convert.ToBase64String(RedSquare())}";
        string lines = string.Join("<br>", Enumerable.Range(1, 120).Select(number => $"Line{number}"));
        PageSetup setup = PageSetup.A4 with { Header = $"<img src='{source}' width='20'>" };
        using var output = new MemoryStream();

        Composition composition = new HtmlToPptx(_fonts).Write($"<p>{lines}</p>", output, setup);

        using var deck = PresentationDocument.Open(output, false);
        IEnumerable<Uri> stored = deck.PresentationPart!.SlideParts
            .SelectMany(slide => slide.ImageParts.Concat(slide.SlideLayoutPart!.ImageParts))
            .Select(part => part.Uri)
            .Distinct();

        Assert.True(composition.Pages.Count > 1);
        Assert.Single(stored);
        Assert.All(deck.PresentationPart!.SlideParts, slide => Assert.Empty(slide.Slide!.Descendants<P.Picture>()));
    }

    /// <summary>A deck must state its own paper rather than inherit the viewer's palette.</summary>
    /// <remarks>
    /// Found on a real document opened in Keynote: the pages that paint no sheet colour of their own came out
    /// BLACK, because the slide fell back to the master, the master to a theme colour named for the system
    /// palette, and a Mac in dark mode resolves that to black. Dark grey text on white paper arrived as dark
    /// grey on black. Nothing in the deck may depend on the reader's system colours.
    /// </remarks>
    [Fact]
    public void Convert_EverySlide_StatesItsOwnPaperInsteadOfTheViewersPalette()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert("<p>x</p>"));
        Slide slide = deck.PresentationPart!.SlideParts.Single().Slide!;
        A.SolidFill? paper = slide.CommonSlideData!.Background!.BackgroundProperties!.GetFirstChild<A.SolidFill>();

        Assert.Equal("FFFFFF", paper!.RgbColorModelHex!.Val!.Value);
        Assert.Empty(deck.PresentationPart.SlideMasterParts.SelectMany(master => master.ThemePart!.Theme!.Descendants<A.SystemColor>()));
    }

    /// <summary>A deck that carries its faces reads the same on a machine that has none of them.</summary>
    /// <remarks>
    /// Only the faces the text sets travel, not every face the application loaded, and they are grouped by
    /// family with a slot apiece, which is how a reader looks one up.
    /// </remarks>
    [Fact]
    public void Convert_ByDefault_CarriesTheFacesTheDocumentIsSetIn()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert($"<p>{Pangram}</p><p><b>Bold</b></p>"));
        PresentationPart presentation = deck.PresentationPart!;
        EmbeddedFontList? carried = presentation.Presentation!.GetFirstChild<P.EmbeddedFontList>();

        EmbeddedFont family = Assert.Single(carried!.Elements<P.EmbeddedFont>());

        Assert.Equal("PT Sans", family.Font!.Typeface!.Value);
        Assert.NotNull(family.RegularFont);
        Assert.NotNull(family.BoldFont);
        Assert.Equal(2, presentation.FontParts.Count());
    }

    [Fact]
    public void Convert_WithEmbeddingTurnedOff_CarriesNoFaces()
    {
        var bare = new PptxOptions { EmbedFonts = false };

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts, bare).Convert($"<p>{Pangram}</p>"));

        Assert.Empty(deck.PresentationPart!.FontParts);
        Assert.Null(deck.PresentationPart.Presentation!.GetFirstChild<P.EmbeddedFontList>());
    }

    [Fact]
    public void Convert_DeckCarryingItsFaces_IsStillValidOpenXml()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert($"<p><i>{Pangram}</i></p>"));

        var faults = new OpenXmlValidator()
            .Validate(deck)
            .Select(fault => $"{fault.Path?.XPath}: {fault.Description}")
            .ToList();

        Assert.Empty(faults);
    }

    [Fact]
    public void Convert_WithATitle_WritesItIntoThePackage()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts, new PptxOptions { Title = "Report" }).Convert("<p>x</p>"));

        Assert.Equal("Report", deck.PackageProperties.Title);
    }

    [Fact]
    public void Convert_UnknownFamily_SaysSoLikeThePdfConverterDoes()
    {
        using var output = new MemoryStream();

        Composition composition = new HtmlToPptx(_fonts).Write("<p style='font-family:Garamond'>x</p>", output);

        Assert.Contains(composition.Notes, note => note is { Kind: NoteKind.FamilySubstituted, Subject: "Garamond" });
    }

    /// <summary>A run's language is what a reader proofs it by, and the deck used to mark every run as English.</summary>
    [Fact]
    public void Convert_WithALanguage_MarksEveryRunWithIt()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts, new PptxOptions { Language = "ru-RU" }).Convert($"<p>{Pangram}</p>"));

        Assert.All(Runs(deck), run => Assert.Equal("ru-RU", run.RunProperties!.Language!.Value));
    }

    [Fact]
    public void Convert_WithoutALanguage_LeavesTheRunsForTheReaderToProof()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert("<p>x</p>"));

        Assert.All(Runs(deck), run => Assert.Null(run.RunProperties!.Language));
    }

    /// <summary>The one test that is not about what the SDK wrote, but about whether the format accepts it.</summary>
    /// <remarks>
    /// Every other test here reads the deck back with the library that produced it, which will happily agree
    /// with itself about a package PowerPoint would refuse to open. The schema is the outside opinion: a deck
    /// hand-assembled from parts — master, layout, theme, slides — is exactly the kind of thing that is well
    /// formed and still invalid, so it is validated with everything on it at once.
    /// </remarks>
    [Fact]
    public void Convert_DeckWithTextPicturesAndBoxes_IsValidOpenXml()
    {
        string source = $"data:image/png;base64,{System.Convert.ToBase64String(RedSquare())}";
        string html = $"<div style='background:#d52321;padding:8pt;border-bottom:1pt solid #000'>"
            + $"<p style='letter-spacing:0.5pt'><b>{Pangram}</b></p>"
            + $"<p><i>slanted</i> and <u>underlined</u></p>"
            + $"<img src='{source}' width='40'></div>";

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert(html));
        var faults = new OpenXmlValidator()
            .Validate(deck)
            .Select(fault => $"{fault.Path?.XPath}: {fault.Description}")
            .ToList();

        Assert.Empty(faults);
    }

    private static PresentationDocument Open(byte[] deck) => PresentationDocument.Open(new MemoryStream(deck), false);

    private static IEnumerable<P.Shape> Shapes(PresentationDocument deck) =>
        deck.PresentationPart!.SlideParts.SelectMany(part => part.Slide!.Descendants<P.Shape>());

    private static IEnumerable<A.Run> Runs(PresentationDocument deck) =>
        deck.PresentationPart!.SlideParts.SelectMany(part => part.Slide!.Descendants<A.Run>());

    private static IEnumerable<string> Texts(PresentationDocument deck) =>
        Runs(deck).Select(run => run.Text?.Text ?? "");

    private static byte[] RedSquare() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x04, 0x08, 0x02, 0x00, 0x00, 0x00, 0x26, 0x93, 0x09,
        0x29, 0x00, 0x00, 0x00, 0x18, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0xF0,
        0x9F, 0x81, 0xE1, 0x3F, 0x03, 0xC3, 0x7F, 0x06, 0x86, 0xFF, 0x0C, 0x00, 0x2A, 0xDB, 0x04, 0xFB,
        0x29, 0x8B, 0x3F, 0xBE, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];
}
