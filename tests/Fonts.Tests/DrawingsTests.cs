using System.Text;
using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts.Tests;

/// <summary>Drawings are judged by their pixels: each test draws a tiny picture and reads the colour where a shape should be.</summary>
public sealed class DrawingsTests : IDisposable
{
    private readonly FontLibrary _library = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));

    private readonly Faces _faces;

    public DrawingsTests() => _faces = new Faces(_library);

    public void Dispose()
    {
        _faces.Dispose();
        _library.Dispose();
    }

    [Fact]
    public void Rasterized_AFilledRectangle_PaintsItsColour()
    {
        SKColor pixel = PixelOf("<svg width='10' height='10'><rect width='10' height='10' fill='#ff0000'/></svg>", 5, 5);

        Assert.Equal(new SKColor(0xFF, 0x00, 0x00), pixel);
    }

    [Fact]
    public void Rasterized_AFillOfNone_LeavesTheGroundTransparent()
    {
        SKColor pixel = PixelOf("<svg width='10' height='10'><rect width='10' height='10' fill='none'/></svg>", 5, 5);

        Assert.Equal(0, pixel.Alpha);
    }

    [Fact]
    public void Rasterized_AGroupFill_ReachesItsChildren()
    {
        SKColor pixel = PixelOf("<svg width='10' height='10'><g fill='rgb(0,128,0)'><circle cx='5' cy='5' r='4'/></g></svg>", 5, 5);

        Assert.Equal(new SKColor(0x00, 0x80, 0x00), pixel);
    }

    [Fact]
    public void Rasterized_ATranslatedShape_MovesWithItsTransform()
    {
        const string drawing = "<svg width='10' height='10'><rect width='5' height='10' fill='black' transform='translate(5 0)'/></svg>";

        Assert.Equal(0, PixelOf(drawing, 2, 5).Alpha);
        Assert.Equal(255, PixelOf(drawing, 7, 5).Alpha);
    }

    [Fact]
    public void Rasterized_AViewBox_ScalesTheDrawingToTheFrame()
    {
        SKColor pixel = PixelOf("<svg width='10' height='10' viewBox='0 0 1 1'><rect width='1' height='1' fill='#0000ff'/></svg>", 9, 9);

        Assert.Equal(new SKColor(0x00, 0x00, 0xFF), pixel);
    }

    [Fact]
    public void Rasterized_AStyleAttribute_WinsOverThePresentationAttribute()
    {
        SKColor pixel = PixelOf("<svg width='10' height='10'><rect width='10' height='10' fill='red' style='fill:#00ff00'/></svg>", 5, 5);

        Assert.Equal(new SKColor(0x00, 0xFF, 0x00), pixel);
    }

    [Fact]
    public void Rasterized_AStrokedLine_PaintsAlongIt()
    {
        SKColor pixel = PixelOf("<svg width='10' height='10'><line x1='0' y1='5' x2='10' y2='5' stroke='black' stroke-width='2'/></svg>", 5, 5);

        Assert.Equal(255, pixel.Alpha);
    }

    [Fact]
    public void Rasterized_ADefinition_IsNotPaintedWhereItStands()
    {
        const string drawing = "<svg width='10' height='10'><defs><rect id='r' width='10' height='10'/></defs></svg>";

        Assert.Equal(0, PixelOf(drawing, 5, 5).Alpha);
    }

    [Fact]
    public void Rasterized_AUse_DrawsWhatItPointsAt()
    {
        const string drawing = "<svg width='10' height='10'><defs><rect id='r' width='10' height='10'/></defs><use href='#r' fill='black'/></svg>";

        Assert.Equal(255, PixelOf(drawing, 5, 5).Alpha);
    }

    [Fact]
    public void Rasterized_ALabel_InksPixelsWithTheLoadedFace()
    {
        byte[] png = Drawings.Rasterized(Drawing("<svg width='60' height='20'><text x='0' y='16' font-size='16'>Шахта</text></svg>"), 60, 20, _faces);

        using SKBitmap bitmap = SKBitmap.Decode(png);
        int inked = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                inked += bitmap.GetPixel(x, y).Alpha > 128 ? 1 : 0;
            }
        }

        Assert.True(inked > 40);
    }

    [Fact]
    public void Load_ASvgDataUriWithoutBase64_ReadsADrawing()
    {
        Image? image = new LocalPictures(null).Load("data:image/svg+xml,%3Csvg%20width%3D%2240%22%20height%3D%2220%22%3E%3C%2Fsvg%3E");

        Assert.NotNull(image);
        Assert.True(image.IsVector);
        Assert.Equal((40, 20), (image.Width, image.Height));
    }

    [Fact]
    public void Load_ASvgFileUnderTheBaseDirectory_ReadsADrawing()
    {
        string root = Directory.CreateTempSubdirectory("composer-svg").FullName;

        try
        {
            File.WriteAllText(Path.Combine(root, "mark.svg"), "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 12'/>");

            Image? image = new LocalPictures(root).Load("mark.svg");

            Assert.True(image?.IsVector);
            Assert.Equal((24, 12), (image?.Width, image?.Height));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private SKColor PixelOf(string markup, int x, int y)
    {
        Image image = Drawing(markup);
        byte[] png = Drawings.Rasterized(image, image.Width, image.Height, _faces);

        using SKBitmap bitmap = SKBitmap.Decode(png);

        return bitmap.GetPixel(x, y);
    }

    private static Image Drawing(string markup) => Svg.Read(Encoding.UTF8.GetBytes(markup)) ?? throw new InvalidOperationException(markup);
}
