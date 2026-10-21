using System.Text;
using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts.Tests.Chaos;

/// <summary>SVG a user can hand the drawing code, which it must draw, refuse or stop on — never crash on.</summary>
public sealed class DrawingsChaosTests : IDisposable
{
    private readonly FontLibrary _fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));

    public void Dispose() => _fonts.Dispose();

    /// <summary>A size Skia cannot allocate makes SKSurface.Create return null, and the public method dereferences it.</summary>
    [Fact]
    public void Rasterized_AtASizeSkiaCannotAllocate_ThrowsAnArgumentException()
    {
        Image image = Svg("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='5' height='5'/></svg>");
        using var faces = new Faces(_fonts);

        Exception? thrown = Record.Exception(() => Drawings.Rasterized(image, 40_000, 40_000, faces));

        Assert.IsNotType<NullReferenceException>(thrown);
    }

    /// <summary>
    /// <c>symbol</c> is in the set of elements never painted, and <c>use</c> reaching one is turned away before it
    /// draws: an icon sprite — the commonest use of <c>use</c> — comes out blank.
    /// </summary>
    [Fact]
    public void UseOfASymbol_DrawsTheSymbol()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'>"
            + "<symbol id='dot' viewBox='0 0 10 10'><rect width='10' height='10' fill='#000'/></symbol>"
            + "<use href='#dot' width='20' height='20'/></svg>";
        using var faces = new Faces(_fonts);

        byte[] png = Drawings.Rasterized(Svg(svg), 20, 20, faces);

        Assert.True(Inked(png) > 0, "the symbol drew nothing");
    }

    /// <summary>
    /// Drawing recursed once per element: 50 000 nested groups — 350 KB — ended the process with a stack overflow
    /// on an 8 MB main thread, and far fewer on a pool thread. Past the budget the rest is left undrawn.
    /// </summary>
    [Fact]
    public async Task DeeplyNestedGroups_AreDrawnAsFarAsTheBudgetGoes()
    {
        string groups = string.Concat(Enumerable.Repeat("<g>", 50_000)) + "<rect width='5' height='5'/>" + string.Concat(Enumerable.Repeat("</g>", 50_000));
        Image drawing = Svg($"<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='2' height='2'/>{groups}</svg>");
        using var faces = new Faces(_fonts);

        byte[] png = await Task.Run(() => Drawings.Rasterized(drawing, 10, 10, faces));

        Assert.True(Inked(png) > 0, "the shape above the deep groups was not drawn either");
    }

    /// <summary>A drawing past the budget is still drawn in part, and the document says so.</summary>
    [Fact]
    public void DrawingPastTheBudget_IsNamedInTheNotes()
    {
        string groups = string.Concat(Enumerable.Repeat("<g>", 300)) + "<rect width='5' height='5'/>" + string.Concat(Enumerable.Repeat("</g>", 300));
        string svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'>{groups}</svg>";
        string html = $"<img src='data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}'>";
        using var faces = new Faces(_fonts);

        Composition composition = new Typesetter(faces, new LocalPictures(null)).Compose(html, PageSetup.A4);
        IEnumerable<Note> notes = Notes.For(composition, _fonts, faces);

        Assert.Contains(notes, note => note.Kind == NoteKind.NestingCapped);
    }

    private static Image Svg(string markup) =>
        Engine.Svg.Read(Encoding.UTF8.GetBytes(markup)) ?? throw new InvalidOperationException("The test drawing did not read as SVG.");

    private static int Inked(byte[] png)
    {
        using SKBitmap bitmap = SKBitmap.Decode(png);
        int inked = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                inked += bitmap.GetPixel(x, y).Alpha > 0 ? 1 : 0;
            }
        }

        return inked;
    }
}
