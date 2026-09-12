using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts.Tests;

public sealed class MemoryPicturesTests
{
    /// <summary>The source is the string the markup writes, not a path: a route the application serves is a fine one.</summary>
    [Fact]
    public void HeldPicture_IsReadUnderTheSourceItWasHeldBy()
    {
        MemoryPictures pictures = new MemoryPictures().Add("/designs/66f0/image", RedSquare());

        Image? picture = pictures.Load("/designs/66f0/image");

        Assert.NotNull(picture);
        Assert.Equal(4, picture.Width);
        Assert.Equal(4, picture.Height);
    }

    [Fact]
    public void SourceNotHeld_IsStillReadAsADataUri()
    {
        var pictures = new MemoryPictures();

        Assert.NotNull(pictures.Load($"data:image/png;base64,{Convert.ToBase64String(RedSquare())}"));
    }

    /// <summary>Nothing is fetched: a source held nowhere is simply not a picture.</summary>
    [Fact]
    public void SourceHeldNowhere_IsNoPicture() => Assert.Null(new MemoryPictures().Load("https://example.org/logo.png"));

    [Fact]
    public void BytesThatAreNotAPicture_AreNoPicture()
    {
        MemoryPictures pictures = new MemoryPictures().Add("broken", [1, 2, 3, 4]);

        Assert.Null(pictures.Load("broken"));
    }

    [Fact]
    public void SourceHeldTwice_KeepsTheLaterBytes()
    {
        MemoryPictures pictures = new MemoryPictures().Add("logo", [1, 2, 3, 4]).Add("logo", RedSquare());

        Assert.NotNull(pictures.Load("logo"));
    }

    private static byte[] RedSquare() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x04, 0x08, 0x02, 0x00, 0x00, 0x00, 0x26, 0x93, 0x09,
        0x29, 0x00, 0x00, 0x00, 0x18, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0xF0,
        0x9F, 0x81, 0xE1, 0x3F, 0x03, 0xC3, 0x7F, 0x06, 0x86, 0xFF, 0x0C, 0x00, 0x2A, 0xDB, 0x04, 0xFB,
        0x29, 0x8B, 0x3F, 0xBE, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];
}
