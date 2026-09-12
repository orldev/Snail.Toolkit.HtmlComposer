using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;
using Snail.Toolkit.HtmlComposer.Pdf;
using Snail.Toolkit.HtmlComposer.Pptx;
using UglyToad.PdfPig;

namespace Snail.Toolkit.HtmlComposer.Documents.Tests;

/// <summary>Whole documents rendered both ways: the corpus a change to the engine is checked against.</summary>
/// <remarks>
/// The documents are synthetic — no client's data and no commercial face ever enters the repository — but
/// they exercise what real ones did: a sheet that bleeds, running heads with a picture, a note in the side
/// margin, a table cut between rows with its heading set again, pictures cropped to a frame, tracked
/// capitals. The page counts are what the documents came to when they were last looked at, so a change
/// that moves one is a change to look at rather than a failure in itself. Every rendering is left under
/// <c>out</c> beside the test assembly, to be opened after a run.
/// </remarks>
public sealed class DocumentsTests : IDisposable
{
    private static readonly Dictionary<string, Func<Document>> Corpus = new()
    {
        ["report"] = Report,
        ["quotation"] = Quotation,
    };

    private readonly FontLibrary _fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));

    private readonly MemoryPictures _pictures = new MemoryPictures()
        .Add("/pictures/mark", Placeholder(96, 96, new SKColor(0x12, 0x12, 0x12)))
        .Add("/pictures/cabin-1", Placeholder(400, 800, new SKColor(0x8A, 0x8F, 0x98)))
        .Add("/pictures/cabin-2", Placeholder(400, 800, new SKColor(0x5A, 0x5E, 0x66)))
        .Add("/pictures/cabin-3", Placeholder(400, 800, new SKColor(0xB4, 0xB8, 0xC0)));

    public void Dispose() => _fonts.Dispose();

    [Theory]
    [InlineData("report", 3)]
    [InlineData("quotation", 2)]
    public void HtmlToPdf_Document_ComesToItsPagesWithoutANote(string name, int pages)
    {
        Document document = Corpus[name]();
        var converter = new HtmlToPdf(_fonts, new PdfOptions { Images = _pictures, Title = document.Title });
        using var output = new MemoryStream();

        Composition composition = converter.Write(document.Sections, output);

        using var pdf = PdfDocument.Open(Kept($"{name}.pdf", output.ToArray()));

        Assert.Equal(pages, composition.Pages.Count);
        Assert.Equal(pages, pdf.NumberOfPages);
        Assert.Empty(composition.Notes);
    }

    [Theory]
    [InlineData("report", 3)]
    [InlineData("quotation", 2)]
    public void HtmlToPptx_Document_WritesASlidePerPageThatIsValidOpenXml(string name, int pages)
    {
        Document document = Corpus[name]();
        var converter = new HtmlToPptx(_fonts, new PptxOptions { Images = _pictures, Title = document.Title });
        using var output = new MemoryStream();

        Composition composition = converter.Write(document.Sections, output);

        using var deck = PresentationDocument.Open(new MemoryStream(Kept($"{name}.pptx", output.ToArray())), false);
        var faults = new OpenXmlValidator()
            .Validate(deck)
            .Select(fault => $"{fault.Path?.XPath}: {fault.Description}")
            .ToList();

        Assert.Equal(pages, composition.Pages.Count);
        Assert.Equal(pages, deck.PresentationPart!.SlideParts.Count());
        Assert.Empty(composition.Notes);
        Assert.Empty(faults);
    }

    private static Document Report()
    {
        PageSetup cover = PageSetup.A4 with { Margins = Edges.All(0), Background = new Rgba(0xD5, 0x23, 0x21) };
        PageSetup body = PageSetup.A4 with
        {
            Header = Markup("report-head.html"),
            Footer = "<div style=\"font-family:'PT Sans';font-size:6.5pt;letter-spacing:0.08em;text-transform:uppercase;color:#5a5a58;margin:0;padding-top:18pt\">Dossier 8FD18287 · page {page} of {pages}</div>",
        };

        return new Document(
            "Dossier 8FD18287",
            [new Section(Markup("report-cover.html"), cover), new Section(Markup("report.html"), body)]);
    }

    private static Document Quotation()
    {
        var sheet = new PageSetup(720, 1040, new Edges(32, 48, 28, 168))
        {
            Side = Markup("quotation-side.html"),
            Footer = "<div style=\"font-family:'PT Sans';font-size:9pt;color:#5a5a58;margin:0;padding-top:8pt\">Northwind Elevators · quotation · {page} / {pages}</div>",
        };

        return new Document("Quotation — Riverside Towers", [new Section(Markup("quotation.html"), sheet)]);
    }

    private static string Markup(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Documents", name));

    /// <summary>Leaves a rendering under <c>out</c> beside the test assembly and hands it back.</summary>
    private static byte[] Kept(string file, byte[] content)
    {
        string folder = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "out")).FullName;
        File.WriteAllBytes(Path.Combine(folder, file), content);

        return content;
    }

    /// <summary>A picture drawn rather than fetched, for the same reason the documents are made up.</summary>
    private static byte[] Placeholder(int width, int height, SKColor colour)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var stroke = new SKPaint { Color = SKColors.White, StrokeWidth = 4, Style = SKPaintStyle.Stroke, IsAntialias = true };

        canvas.Clear(colour);
        canvas.DrawRect(8, 8, width - 16, height - 16, stroke);
        canvas.DrawLine(8, 8, width - 8, height - 8, stroke);
        canvas.DrawLine(width - 8, 8, 8, height - 8, stroke);

        using var image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);

        return png.ToArray();
    }

    /// <summary>One document of the corpus: what the file says it is, and its sections.</summary>
    private sealed record Document(string Title, IReadOnlyList<Section> Sections);
}
