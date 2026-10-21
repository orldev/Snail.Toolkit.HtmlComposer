using DocumentFormat.OpenXml.Packaging;
using Snail.Toolkit.HtmlComposer.Fonts;

namespace Snail.Toolkit.HtmlComposer.Pptx.Tests;

public sealed class SvgTests : IDisposable
{
    private readonly FontLibrary _fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));

    public void Dispose() => _fonts.Dispose();

    /// <summary>A slide holds pictures, so a drawing reaches the deck as PNG pixels drawn four to its pixel.</summary>
    [Fact]
    public void Convert_InlineSvg_BecomesAPngPictureOnTheSlide()
    {
        byte[] bytes = new HtmlToPptx(_fonts).Convert("<svg width='40' height='20'><rect width='40' height='20' fill='#ff3b00'/></svg>");

        using var deck = PresentationDocument.Open(new MemoryStream(bytes), false);
        ImagePart picture = Assert.Single(deck.PresentationPart!.SlideParts.Single().ImageParts);

        using Stream stream = picture.GetStream();
        using var png = SkiaSharp.SKBitmap.Decode(stream);

        Assert.Equal("image/png", picture.ContentType);
        Assert.Equal((160, 80), (png.Width, png.Height));
    }
}
