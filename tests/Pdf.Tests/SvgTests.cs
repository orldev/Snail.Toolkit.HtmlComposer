using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using PdfPage = UglyToad.PdfPig.Content.Page;

namespace Snail.Toolkit.HtmlComposer.Pdf.Tests;

public sealed class SvgTests : IDisposable
{
    private const string Shaft = """
        <svg viewBox="0 0 120 60" style="width:240pt">
          <rect x="10" y="10" width="40" height="40" fill="#0a0a0a"/>
          <line x1="60" y1="30" x2="110" y2="30" stroke="#ff3b00" stroke-width="2"/>
          <text x="60" y="55" font-family="PT Sans" font-size="8">Шахта</text>
        </svg>
        """;

    private readonly FontLibrary _fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));

    public void Dispose() => _fonts.Dispose();

    /// <summary>A drawing reaches the PDF as paths, not as a picture of itself.</summary>
    [Fact]
    public void Convert_InlineSvg_DrawsShapesRatherThanAPicture()
    {
        using var document = PdfDocument.Open(new HtmlToPdf(_fonts).Convert(Shaft));
        PdfPage page = document.GetPage(1);

        Assert.Empty(page.GetImages());
        Assert.NotEmpty(page.Paths);
    }

    /// <summary>The label of a drawing is text in the file, set in the document's face, so it is searched and copied.</summary>
    [Fact]
    public void Convert_SvgLabel_ReadsBackAsText()
    {
        using var document = PdfDocument.Open(new HtmlToPdf(_fonts).Convert(Shaft));

        Assert.Contains("Шахта", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_SvgLabel_LandsInsideTheBoxOfItsDrawing()
    {
        var converter = new HtmlToPdf(_fonts);
        ImageBox box = Assert.Single(converter.Typeset(Shaft).Pages[0].Images);

        using var document = PdfDocument.Open(converter.Convert(Shaft));
        PdfPage page = document.GetPage(1);
        Word label = Assert.Single(page.GetWords(), word => word.Text == "Шахта");
        double top = page.Height - label.BoundingBox.Top;

        Assert.InRange(label.BoundingBox.Left, box.Bounds.X, box.Bounds.Right);
        Assert.InRange(top, box.Bounds.Y, box.Bounds.Bottom);
    }

    [Fact]
    public void Write_ASvgFromAFile_LeavesNoNote()
    {
        string root = Directory.CreateTempSubdirectory("composer-svg-pdf").FullName;

        try
        {
            File.WriteAllText(Path.Combine(root, "mark.svg"), "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 10 10'><circle cx='5' cy='5' r='5'/></svg>");
            using var output = new MemoryStream();

            Composition composition = new HtmlToPdf(_fonts, new PdfOptions { BaseDirectory = root }).Write("<img src='mark.svg' width='40'>", output);

            Assert.Empty(composition.Notes);
            Assert.True(Assert.Single(composition.Pages[0].Images).Image.IsVector);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
