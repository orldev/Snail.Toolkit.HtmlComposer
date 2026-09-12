using BenchmarkDotNet.Attributes;
using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;
using Snail.Toolkit.HtmlComposer.Pdf;
using Snail.Toolkit.HtmlComposer.Pptx;

namespace Snail.Toolkit.HtmlComposer.Documents.Benchmarks;

/// <summary>The corpus documents set end to end: the cost a change to the engine is weighed against.</summary>
/// <remarks>
/// The report is three pages with a cover, running heads, a table cut between pages and pictures cropped to
/// a frame; the quotation is two pages with a note in the side margin. Typesetting alone is timed apart from
/// writing a PDF, because the engine is where a change lands and the writer only draws what it settled.
/// </remarks>
[MemoryDiagnoser]
public class Composing
{
    private FontLibrary _fonts = null!;

    private HtmlToPdf _converter = null!;

    private HtmlToPptx _deck = null!;

    private IReadOnlyList<Section> _report = null!;

    private IReadOnlyList<Section> _quotation = null!;

    private IReadOnlyList<Section> _long = null!;

    private IReadOnlyList<Section> _longBare = null!;

    private IReadOnlyList<Section> _longWithMark = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));

        MemoryPictures pictures = new MemoryPictures()
            .Add("/pictures/mark", Placeholder(96, 96, new SKColor(0x12, 0x12, 0x12)))
            .Add("/pictures/cabin-1", Placeholder(400, 800, new SKColor(0x8A, 0x8F, 0x98)))
            .Add("/pictures/cabin-2", Placeholder(400, 800, new SKColor(0x5A, 0x5E, 0x66)))
            .Add("/pictures/cabin-3", Placeholder(400, 800, new SKColor(0xB4, 0xB8, 0xC0)));

        _converter = new HtmlToPdf(_fonts, new PdfOptions { Images = pictures });
        _deck = new HtmlToPptx(_fonts, new PptxOptions { Images = pictures });
        _report = Report();
        _quotation = Quotation();
        _long = Long(withRunningLines: true);
        _longBare = Long(withRunningLines: false);
        _longWithMark = Long(withRunningLines: true, withMark: true);
    }

    [GlobalCleanup]
    public void Cleanup() => _fonts.Dispose();

    [Benchmark]
    public Composition ReportTypeset() => _converter.Typeset(_report);

    [Benchmark]
    public Composition QuotationTypeset() => _converter.Typeset(_quotation);

    [Benchmark]
    public byte[] ReportPdf() => _converter.Convert(_report);

    /// <summary>Forty pages of plain paragraphs with a running head and foot on every one.</summary>
    [Benchmark]
    public Composition LongTypeset() => _converter.Typeset(_long);

    /// <summary>The same forty pages without running lines: what the lines themselves cost is the difference.</summary>
    [Benchmark]
    public Composition LongTypesetBare() => _converter.Typeset(_longBare);

    /// <summary>The report written as a deck, which nothing measured until now: the PDF had a benchmark and the deck had none.</summary>
    [Benchmark]
    public byte[] ReportPptx() => _deck.Convert(_report);

    /// <summary>
    /// Forty pages whose running head carries the same picture, which the deck stores once however many
    /// slides show it: the cost of deciding that they are the same picture is what this weighs.
    /// </summary>
    [Benchmark]
    public byte[] LongPptx() => _deck.Convert(_longWithMark);

    private static IReadOnlyList<Section> Report()
    {
        PageSetup cover = PageSetup.A4 with { Margins = Edges.All(0), Background = new Rgba(0xD5, 0x23, 0x21) };
        PageSetup body = PageSetup.A4 with
        {
            Header = Markup("report-head.html"),
            Footer = "<div style=\"font-family:'PT Sans';font-size:6.5pt;letter-spacing:0.08em;text-transform:uppercase;color:#5a5a58;margin:0;padding-top:18pt\">Dossier 8FD18287 · page {page} of {pages}</div>",
        };

        return [new Section(Markup("report-cover.html"), cover), new Section(Markup("report.html"), body)];
    }

    private static IReadOnlyList<Section> Quotation()
    {
        var sheet = new PageSetup(720, 1040, new Edges(32, 48, 28, 168))
        {
            Side = Markup("quotation-side.html"),
            Footer = "<div style=\"font-family:'PT Sans';font-size:9pt;color:#5a5a58;margin:0;padding-top:8pt\">Northwind Elevators · quotation · {page} / {pages}</div>",
        };

        return [new Section(Markup("quotation.html"), sheet)];
    }

    /// <summary>A long document made of one paragraph repeated, so that its length is the only thing about it.</summary>
    private static IReadOnlyList<Section> Long(bool withRunningLines, bool withMark = false)
    {
        const string paragraph = "<p>Съешь же ещё этих мягких французских булок, да выпей чаю. The quick brown fox jumps over "
            + "the lazy dog, and the lines that follow are set in the same face at the same size, so every page holds "
            + "the same number of them and the running lines are set the same number of times.</p>";
        string html = $"<style>body {{ font-family: 'PT Sans'; font-size: 11pt }}</style>{string.Concat(Enumerable.Repeat(paragraph, 600))}";
        string mark = withMark ? "<img src=\"/pictures/mark\" width=\"12\" height=\"12\"> " : string.Empty;
        PageSetup sheet = withRunningLines
            ? PageSetup.A4 with
            {
                Header = $"<div style=\"font-family:'PT Sans';font-size:8pt;color:#5a5a58\">{mark}Long document · page {{page}} of {{pages}}</div>",
                Footer = "<div style=\"font-family:'PT Sans';font-size:8pt;color:#5a5a58;padding-top:12pt\">{page}</div>",
            }
            : PageSetup.A4;

        return [new Section(html, sheet)];
    }

    private static string Markup(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Documents", name));

    /// <summary>A picture drawn rather than fetched, as the corpus tests draw theirs.</summary>
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
}
