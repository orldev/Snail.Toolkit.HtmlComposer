using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts.Tests;

/// <summary>Faces cut down to the glyphs a composition draws: smaller, and drawn exactly as the whole faces are.</summary>
public sealed class SubsetTests : IDisposable
{
    private static readonly string Regular = Path.Combine(AppContext.BaseDirectory, "Fonts", "PT_Sans-Web-Regular.ttf");

    private readonly FontLibrary _fonts = new FontLibrary().AddFile(Regular);

    public void Dispose() => _fonts.Dispose();

    [Fact]
    public void Subset_HoldsFarLessThanTheWholeFace()
    {
        using var faces = new Faces(_fonts);
        Composition composition = Composed(faces, "<p>Hi</p>");

        using Faces subset = faces.Subset(composition);

        Assert.True(SizeOf(subset) < SizeOf(faces) / 4, $"the cut face holds {SizeOf(subset)} bytes of {SizeOf(faces)}");
    }

    /// <summary>Every glyph keeps its number and its advance, so a run cut down is set exactly as it was measured.</summary>
    [Fact]
    public void Subset_KeepsEveryGlyphNumberAndAdvance()
    {
        using var faces = new Faces(_fonts);
        Composition composition = Composed(faces, "<p>Hi</p>");

        using Faces subset = faces.Subset(composition);

        SKFont whole = faces.For(Body);
        SKFont cut = subset.For(Body);
        ushort[] every = [.. Enumerable.Range(0, whole.Typeface.GlyphCount).Select(glyph => (ushort)glyph)];

        Assert.Equal(whole.Typeface.GlyphCount, cut.Typeface.GlyphCount);
        Assert.Equal(whole.GetGlyphWidths(every), cut.GetGlyphWidths(every));
        Assert.Equal(whole.GetGlyphs("Шахта"), cut.GetGlyphs("Шахта"));
    }

    /// <summary>
    /// <c>½</c> and <c>À</c> are composites — outlines placed from other glyphs — and a cut that kept the composite
    /// but not its parts would draw nothing in their place.
    /// </summary>
    [Theory]
    [InlineData("Шахта Ж-5")]
    [InlineData("½ À 20 %")]
    public void Subset_DrawsTheTextAsTheWholeFaceDoes(string text)
    {
        using var faces = new Faces(_fonts);
        Composition composition = Composed(faces, $"<p>{text}</p>");

        using Faces subset = faces.Subset(composition);

        Assert.Equal(Pixels(faces.For(Body), text), Pixels(subset.For(Body), text));
    }

    /// <summary>A label of a drawing is set in the document's faces too, so its glyphs have to be in the cut.</summary>
    [Fact]
    public void Subset_KeepsTheGlyphsOfADrawingsLabels()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='80' height='20'><text x='2' y='14' font-family='PT Sans' font-size='12'>Ёж</text></svg>";
        using var faces = new Faces(_fonts);
        Composition composition = Composed(faces, svg);

        using Faces subset = faces.Subset(composition);

        Assert.Equal(Pixels(faces.For(Body), "Ёж"), Pixels(subset.For(Body), "Ёж"));
    }

    /// <summary>Bit 8 of <c>fsType</c> is the maker saying the face may be embedded only whole.</summary>
    [Fact]
    public void FaceThatForbidsSubsetting_StaysWhole()
    {
        byte[] forbidding = WithFsType(File.ReadAllBytes(Regular), 0x0100);
        using var library = new FontLibrary().Add(new MemoryStream(forbidding));
        using var faces = new Faces(library);
        Composition composition = Composed(faces, "<p>Hi</p>");

        using Faces subset = faces.Subset(composition);

        Assert.Equal(SizeOf(faces), SizeOf(subset));
    }

    private static readonly Font Body = new("PT Sans", 24);

    private static Composition Composed(Faces faces, string html) =>
        new Typesetter(faces, new LocalPictures(null)).Compose($"<div style=\"font-family:'PT Sans'\">{html}</div>", PageSetup.A5);

    private static long SizeOf(Faces faces)
    {
        using SKStreamAsset stream = faces.For(Body).Typeface.OpenStream();

        return stream.Length;
    }

    private static byte[] Pixels(SKFont font, string text)
    {
        using var bitmap = new SKBitmap(400, 60);
        using var canvas = new SKCanvas(bitmap);
        using var ink = new SKPaint { IsAntialias = true, Color = SKColors.Black };

        canvas.Clear(SKColors.White);
        canvas.DrawText(text, 4, 40, SKTextAlign.Left, font, ink);

        return bitmap.Bytes;
    }

    /// <summary>The face with its embedding flags set as given; the table checksum is left stale, which no reader checks.</summary>
    private static byte[] WithFsType(byte[] font, ushort flags)
    {
        int count = (font[4] << 8) | font[5];

        for (int index = 0; index < count; index++)
        {
            int record = 12 + (index * 16);

            if (font[record] == 'O' && font[record + 1] == 'S' && font[record + 2] == '/' && font[record + 3] == '2')
            {
                int offset = (font[record + 8] << 24) | (font[record + 9] << 16) | (font[record + 10] << 8) | font[record + 11];

                font[offset + 8] = (byte)(flags >> 8);
                font[offset + 9] = (byte)flags;
            }
        }

        return font;
    }
}
