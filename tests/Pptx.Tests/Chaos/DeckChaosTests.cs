using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Snail.Toolkit.HtmlComposer.Pptx.Tests.Chaos;

/// <summary>Documents the deck writer meets in the wild that it must still write as a deck PowerPoint opens.</summary>
public sealed class DeckChaosTests : IDisposable
{
    private readonly FontLibrary _fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));

    public void Dispose() => _fonts.Dispose();

    /// <summary>
    /// The engine keeps a control character from the data — <c>&amp;#1;</c>, a stray byte from a database — and
    /// the Open XML text element refuses it: the whole conversion throws for one invisible character.
    /// </summary>
    [Theory]
    [InlineData("<p>a&#1;b</p>", "ab")]
    [InlineData("<table><tr><td>x&#2;y</td></tr></table>", "xy")]
    [InlineData("<p>a\uD800b</p>", "b")]
    public void ControlCharacterInText_IsLeftOutRatherThanFailingTheDeck(string html, string expected)
    {
        var options = new PptxOptions { Title = "title\u0001" };

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts, options).Convert(html));

        string written = string.Concat(deck.PresentationPart!.SlideParts.SelectMany(part => part.Slide!.Descendants<A.Text>()).Select(text => text.Text));

        Assert.Contains(expected, written, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sheet colour is drawn as a shape the size of the slide, and the running lines now live on the layout
    /// beneath every shape of the slide: a sheet with a colour hides its own header, footer and slide number.
    /// </summary>
    [Fact]
    public void SheetColour_LeavesTheRunningLinesVisible()
    {
        PageSetup setup = PageSetup.A5 with { Background = new Rgba(250, 245, 230), Footer = "<p>Page {page} of {pages}</p>" };

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert("<p>body</p>", setup));

        P.Presentation presentation = deck.PresentationPart!.Presentation!;
        long width = presentation.SlideSize!.Cx!.Value;
        long height = presentation.SlideSize.Cy!.Value;
        var covering = deck.PresentationPart.SlideParts.First().Slide!.Descendants<P.Shape>()
            .Select(shape => shape.ShapeProperties?.Transform2D?.Extents)
            .Where(extents => extents?.Cx?.Value >= width && extents.Cy?.Value >= height)
            .ToList();

        Assert.Empty(covering);
    }

    /// <summary>A header and a footer that both print the page number: only the first becomes the slide number, the second stays «1» on every slide.</summary>
    [Fact]
    public void PageNumberInHeaderAndFooter_FollowsTheSlideInBoth()
    {
        string lines = string.Join("<br>", Enumerable.Range(1, 120).Select(number => $"Line{number}"));
        PageSetup setup = PageSetup.A5 with { Header = "<p>Head {page}</p>", Footer = "<p>Page {page} of {pages}</p>" };

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert($"<p>{lines}</p>", setup));

        P.SlideLayout layout = deck.PresentationPart!.SlideParts.First().SlideLayoutPart!.SlideLayout!;

        Assert.Equal(2, layout.Descendants<A.Field>().Count(field => field.Type == "slidenum"));
    }

    /// <summary>Markup the schema of the deck does not allow once it is written, and PowerPoint offers to repair.</summary>
    [Theory]
    [InlineData("<table><tr></tr></table><p>after</p>")]
    [InlineData("<p>a<span style='font-size:0'>b</span>c</p>")]
    [InlineData("<p style='line-height:2000pt'>tall</p>")]
    public void UnusualMarkup_StillWritesAValidDeck(string html)
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert(html));

        var faults = new OpenXmlValidator().Validate(deck).Select(fault => $"{fault.Path?.XPath}: {fault.Description}").ToList();

        Assert.Empty(faults);
    }

    /// <summary>A deck has one slide size; a landscape section after a portrait one is cut off at the portrait width, and nothing says so.</summary>
    [Fact]
    public void SectionsOfTwoSizes_SayThatTheDeckHasOne()
    {
        Section[] sections = [new("<p>portrait</p>", PageSetup.A4), new("<p>landscape</p>", PageSetup.A4.Landscape())];
        using var output = new MemoryStream();

        Composition composition = new HtmlToPptx(_fonts).Write(sections, output);

        Assert.NotEmpty(composition.Notes);
    }

    /// <summary>A coloured block inside a coloured cell lay under the table, so the cell's fill hid it.</summary>
    [Fact]
    public void BlockInsideAColouredCell_IsDrawnOverTheTable()
    {
        const string html = "<table><tr><td style='background:#ff0'><div style='background:#0f0'>inner</div></td></tr></table>";

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert(html));

        var shapes = deck.PresentationPart!.SlideParts.First().Slide!.CommonSlideData!.ShapeTree!.ChildElements.ToList();
        int table = shapes.FindIndex(shape => shape is P.GraphicFrame);
        int inner = shapes.FindLastIndex(shape => shape is P.Shape box && box.ShapeProperties?.GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value == "00FF00");

        Assert.True(table >= 0 && inner > table, $"the table is shape {table}, the green block {inner}");
    }

    /// <summary>A WebP picture was stored under a PNG content type, which a reader shows as a broken picture.</summary>
    [Fact]
    public void WebPicture_IsStoredAsAPictureEveryReaderOpens()
    {
        using var bitmap = new SkiaSharp.SKBitmap(4, 4);
        bitmap.Erase(SkiaSharp.SKColors.Red);
        using SkiaSharp.SKData webp = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Webp, 90);
        string html = $"<img src='data:image/webp;base64,{System.Convert.ToBase64String(webp.ToArray())}' style='width:20pt;height:20pt'>";

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert(html));

        ImagePart picture = Assert.Single(deck.PresentationPart!.SlideParts.First().ImageParts);
        using Stream stored = picture.GetStream();
        byte[] head = new byte[4];
        stored.ReadExactly(head);

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], head);
    }

    /// <summary>A gap made by a margin was taken for a stretched space and put a word break into the middle of a word.</summary>
    [Fact]
    public void MarginBetweenRuns_IsNotASpace()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert("<p>one<span style='margin-left:10px'>two</span></p>"));

        string written = string.Concat(deck.PresentationPart!.SlideParts.First().Slide!.Descendants<A.Text>().Select(text => text.Text));

        Assert.Equal("onetwo", written);
    }

    /// <summary>
    /// Relationship ids were drawn at random, the properties part was named by a fresh GUID and every zip entry
    /// carried the time it was written: one document never gave the same deck twice.
    /// </summary>
    [Fact]
    public void SameDocument_GivesTheSameBytes()
    {
        var options = new PptxOptions { Title = "Quote", Author = "Sales" };
        PageSetup setup = PageSetup.A5 with { Footer = "<p>{page} / {pages}</p>" };
        const string html = "<h1>Title</h1><table><tr><td>a</td><td>b</td></tr></table>";

        byte[] first = new HtmlToPptx(_fonts, options).Convert(html, setup);
        byte[] second = new HtmlToPptx(_fonts, options).Convert(html, setup);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(first));
        using PresentationDocument deck = Open(first);

        Assert.Equal(first, second);
        Assert.All(archive.Entries, entry => Assert.Equal(1980, entry.LastWriteTime.Year));
        Assert.Equal("Quote", deck.PackageProperties.Title);
        Assert.Equal("Sales", deck.PackageProperties.Creator);
        Assert.Empty(new OpenXmlValidator().Validate(deck));
    }

    private static PresentationDocument Open(byte[] deck) => PresentationDocument.Open(new MemoryStream(deck), false);
}
