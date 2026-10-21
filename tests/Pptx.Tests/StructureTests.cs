using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Snail.Toolkit.HtmlComposer.Pptx.Tests;

/// <summary>A deck people edit: tables as tables, blocks of text as text boxes, running lines on the layout.</summary>
public sealed class StructureTests : IDisposable
{
    private const string Specification =
        "<table style='border-collapse:collapse'>"
        + "<thead><tr><th style='width:40pt;border-bottom:1pt solid #121212'>No</th><th style='width:200pt;border-bottom:1pt solid #121212'>Name</th>"
        + "<th style='width:80pt;border-bottom:1pt solid #121212;text-align:right'>Sum</th></tr></thead>"
        + "<tbody><tr><td style='border-bottom:0.5pt solid #d8d8d4'>01</td><td style='border-bottom:0.5pt solid #d8d8d4'><div><b>Lift</b></div><div>details</div></td>"
        + "<td style='border-bottom:0.5pt solid #d8d8d4;text-align:right'>100</td></tr>"
        + "<tr><td colspan='2' style='vertical-align:middle'>Total</td><td style='text-align:right'>100</td></tr></tbody></table>";

    private readonly FontLibrary _fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));

    public void Dispose() => _fonts.Dispose();

    [Fact]
    public void Table_IsWrittenAsOnePowerPointTableWithAColumnPerDeclaredWidth()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert(Specification));

        A.Table table = Assert.Single(Slide(deck).Descendants<A.Table>());

        Assert.Equal([40 * 12700L, 200 * 12700L, 80 * 12700L], [.. table.TableGrid!.Elements<A.GridColumn>().Select(column => column.Width!.Value)]);
        Assert.Equal(3, table.Elements<A.TableRow>().Count());
        Assert.Equal("{2D5ABB26-0587-4C30-8999-92F81FD0307C}", table.TableProperties!.GetFirstChild<A.TableStyleId>()!.Text);
    }

    [Fact]
    public void CellSpanningColumns_IsWrittenOnce_AndTheColumnsItCoversAreMerged()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert(Specification));

        A.TableRow total = Slide(deck).Descendants<A.TableRow>().Last();
        var cells = total.Elements<A.TableCell>().ToList();

        Assert.Equal(3, cells.Count);
        Assert.Equal(2, cells[0].GridSpan!.Value);
        Assert.True(cells[1].HorizontalMerge!.Value);
        Assert.Equal(A.TextAnchoringTypeValues.Center, cells[0].TableCellProperties!.Anchor!.Value);
    }

    /// <summary>The rule under the heading is one point, the rule under a position half a point; sides are never drawn.</summary>
    [Fact]
    public void Cells_DrawOnlyTheRulesAboveAndBelowThem()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert(Specification));

        var rows = Slide(deck).Descendants<A.TableRow>().ToList();
        A.TableCellProperties heading = rows[0].Elements<A.TableCell>().First().TableCellProperties!;
        A.TableCellProperties position = rows[1].Elements<A.TableCell>().First().TableCellProperties!;

        Assert.Equal(12700, heading.BottomBorderLineProperties!.Width!.Value);
        Assert.Equal(12700, position.TopBorderLineProperties!.Width!.Value);
        Assert.Equal(6350, position.BottomBorderLineProperties!.Width!.Value);
        Assert.NotNull(position.LeftBorderLineProperties!.GetFirstChild<A.NoFill>());
        Assert.NotNull(position.RightBorderLineProperties!.GetFirstChild<A.NoFill>());
    }

    [Fact]
    public void CellOfTwoBlocks_IsOneTextBodyOfTwoParagraphs_EachInItsOwnFace()
    {
        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert(Specification));

        A.TableCell name = Slide(deck).Descendants<A.TableRow>().ElementAt(1).Elements<A.TableCell>().ElementAt(1);
        var paragraphs = name.TextBody!.Elements<A.Paragraph>().ToList();

        Assert.Equal(["Lift", "details"], [.. paragraphs.Select(paragraph => paragraph.InnerText)]);
        Assert.True(paragraphs[0].Elements<A.Run>().Single().RunProperties!.Bold!.Value);
        Assert.False(paragraphs[1].Elements<A.Run>().Single().RunProperties!.Bold!.Value);
    }

    [Fact]
    public void PictureInACell_IsTheCellsFill_NotAPictureLaidOverTheTable()
    {
        string source = $"data:image/png;base64,{System.Convert.ToBase64String(RedSquare())}";
        string html = $"<table><tr><td style='padding:4pt'><img src='{source}' style='width:24pt;height:12pt'></td><td>t</td></tr></table>";

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert(html));

        A.TableCell cell = Slide(deck).Descendants<A.TableCell>().First();
        A.BlipFill fill = Assert.IsType<A.BlipFill>(cell.TableCellProperties!.GetFirstChild<A.BlipFill>());

        Assert.Empty(Slide(deck).Descendants<P.Picture>());
        Assert.True(fill.GetFirstChild<A.Stretch>()!.FillRectangle!.Left!.Value > 0);
    }

    /// <summary>A paragraph that wraps comes back as one paragraph in one box, with the space the wrap swallowed.</summary>
    [Fact]
    public void BlockOfText_IsOneWrappingTextBox_HoldingItsParagraphs()
    {
        string words = string.Join(' ', Enumerable.Repeat("word", 60));

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert($"<p>{words}<br>after</p>"));

        P.Shape box = Assert.Single(Slide(deck).Descendants<P.Shape>());
        var paragraphs = box.TextBody!.Elements<A.Paragraph>().ToList();

        Assert.Equal(A.TextWrappingValues.Square, box.TextBody.BodyProperties!.Wrap!.Value);
        Assert.Equal([words, "after"], [.. paragraphs.Select(paragraph => paragraph.InnerText)]);
    }

    [Fact]
    public void TableAcrossSlides_StartsAgainWithItsHeadingOnTheNextSlide()
    {
        string rows = string.Concat(Enumerable.Range(1, 80).Select(number => $"<tr><td>row {number}</td></tr>"));
        string html = $"<table><thead><tr><th>Heading</th></tr></thead><tbody>{rows}</tbody></table>";

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts).Convert(html));

        var slides = deck.PresentationPart!.SlideParts.ToList();

        Assert.True(slides.Count > 1);
        Assert.All(slides, slide => Assert.Equal(
            "Heading",
            Assert.Single(slide.Slide!.Descendants<A.Table>()).Elements<A.TableRow>().First().InnerText));
    }

    /// <summary>The footer is drawn once, on the layout; each slide only says it shows the layout's slide number.</summary>
    [Fact]
    public void RunningLines_AreOnTheLayout_AndThePageNumberIsPowerPointsSlideNumber()
    {
        string lines = string.Join("<br>", Enumerable.Range(1, 120).Select(number => $"Line{number}"));
        PageSetup setup = PageSetup.A4 with { Footer = "<p style='text-align:right'>{page} / {pages}</p>" };
        using var output = new MemoryStream();

        Composition composition = new HtmlToPptx(_fonts).Write($"<p>{lines}</p>", output, setup);

        using var deck = PresentationDocument.Open(output, false);
        var slides = deck.PresentationPart!.SlideParts.ToList();
        P.SlideLayout layout = Assert.Single(slides.Select(slide => slide.SlideLayoutPart!).Distinct()).SlideLayout!;

        Assert.True(composition.Pages.Count > 1);
        Assert.Contains(layout.Descendants<A.Field>(), field => field.Type == "slidenum");
        Assert.Contains($"{composition.Pages.Count}", layout.Descendants<A.Text>().Select(text => text.Text));
        Assert.All(slides, slide => Assert.Contains(
            slide.Slide!.Descendants<P.PlaceholderShape>(),
            placeholder => placeholder.Type! == P.PlaceholderValues.SlideNumber));
        Assert.All(slides, slide => Assert.DoesNotContain(" / ", slide.Slide!.InnerText, StringComparison.Ordinal));
    }

    [Fact]
    public void RenamedFamily_IsNamedAsTheOptionsSay_AndNotCarried()
    {
        var options = new PptxOptions { Typefaces = new Dictionary<string, string> { ["PT Sans"] = "Arial" } };

        using PresentationDocument deck = Open(new HtmlToPptx(_fonts, options).Convert("<p>text</p>"));

        Assert.All(Slide(deck).Descendants<A.LatinFont>(), font => Assert.Equal("Arial", font.Typeface));
        Assert.Empty(deck.PresentationPart!.FontParts);
    }

    [Fact]
    public void DeckWithTablesTextAndRunningLines_IsValidOpenXml()
    {
        string source = $"data:image/png;base64,{System.Convert.ToBase64String(RedSquare())}";
        PageSetup setup = PageSetup.A4 with
        {
            Header = $"<div style='border-bottom:1pt solid #000'><img src='{source}' width='10'> Brand</div>",
            Footer = "<table style='width:100%'><tr><td>site</td><td style='text-align:right'>{page} / {pages}</td></tr></table>",
        };
        string html = $"<h1>Title</h1><p>one<br>two</p>{Specification}"
            + $"<table><tr><td><img src='{source}' style='width:24pt;height:12pt'></td><td>t</td></tr></table>";
        using var output = new MemoryStream();

        new HtmlToPptx(_fonts).Write(html, output, setup);

        using var deck = PresentationDocument.Open(output, false);
        var faults = new OpenXmlValidator()
            .Validate(deck)
            .Select(fault => $"{fault.Path?.XPath}: {fault.Description}")
            .ToList();

        Assert.Empty(faults);
    }

    private static PresentationDocument Open(byte[] deck) => PresentationDocument.Open(new MemoryStream(deck), false);

    private static P.Slide Slide(PresentationDocument deck) => deck.PresentationPart!.SlideParts.First().Slide!;

    private static byte[] RedSquare() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x04, 0x08, 0x02, 0x00, 0x00, 0x00, 0x26, 0x93, 0x09,
        0x29, 0x00, 0x00, 0x00, 0x18, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0xF0,
        0x9F, 0x81, 0xE1, 0x3F, 0x03, 0xC3, 0x7F, 0x06, 0x86, 0xFF, 0x0C, 0x00, 0x2A, 0xDB, 0x04, 0xFB,
        0x29, 0x8B, 0x3F, 0xBE, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];
}
