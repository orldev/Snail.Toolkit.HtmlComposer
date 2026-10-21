using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;

namespace Snail.Toolkit.HtmlComposer.Pdf;

/// <summary>Draws composed pages onto a PDF canvas, in the order the engine lists them.</summary>
/// <remarks>
/// A plain reader of the composition: every position, size and break was settled by the engine, and the press
/// only turns boxes into rectangles, spans into glyph runs, pictures into images and drawings into shapes.
/// Each picture is decoded once per document, because the writer stores an image once per decoded instance and
/// a logo repeated on every page would otherwise be stored on every page.
/// </remarks>
internal static class Press
{
    private const string Producer = "Snail.Toolkit.HtmlComposer";

    /// <summary>Prints the pages into memory first, then copies the finished file into the stream.</summary>
    /// <remarks>
    /// Skia writes a PDF through a native callback, and a stream given to it directly is written from inside that
    /// callback: an exception from the stream — a client gone, a full disk, a response body that refuses
    /// synchronous writes — crossed the native frame and aborted the process, a web server with it. Written into
    /// Skia's own memory and copied out by managed code, a failing stream throws where the caller can catch it.
    /// <see cref="Converter{TOptions}.Convert(IReadOnlyList{Section}, CancellationToken)"/> held the whole file in
    /// memory already, so only a caller of <c>Write</c> gives up the streaming it never really had: Skia keeps
    /// every page until it closes the document.
    /// <para>
    /// The pages are drawn with faces cut down to the glyphs they hold, because the PDF writer embeds a face whole:
    /// a page of two letters carried all 443 KB of PT Sans.
    /// </para>
    /// </remarks>
    public static void Print(Composition composition, Stream output, Faces fonts, PdfOptions options, CancellationToken ct)
    {
        using Faces subset = fonts.Subset(composition, ct);
        using var buffer = new SKDynamicMemoryWStream();

        Pages(composition, buffer, subset, options, ct);

        using SKData file = buffer.DetachAsData();
        using Stream bytes = file.AsStream();

        bytes.CopyTo(output);
    }

    private static void Pages(Composition composition, SKWStream target, Faces fonts, PdfOptions options, CancellationToken ct)
    {
        using var pictures = new Decoded();
        using var ink = new SKPaint { IsAntialias = true };
        using var document = SKDocument.CreatePdf(target, MetadataOf(options));

        foreach (Page page in composition.Pages)
        {
            ct.ThrowIfCancellationRequested();

            SKCanvas canvas = document.BeginPage(page.Width, page.Height);
            Draw(canvas, page, fonts, pictures, ink, ct);
            document.EndPage();
        }

        document.Close();
    }

    private static SKDocumentPdfMetadata MetadataOf(PdfOptions options) => SKDocumentPdfMetadata.Default with
    {
        Title = options.Title,
        Author = options.Author,
        Subject = options.Subject,
        Keywords = options.Keywords,
        Creator = options.Creator,
        Producer = Producer,
    };

    private static void Draw(SKCanvas canvas, Page page, Faces fonts, Decoded pictures, SKPaint ink, CancellationToken ct)
    {
        foreach (RenderBox? box in page.Boxes.Where(box => box.IsPainted))
        {
            DrawBox(canvas, box);
        }

        foreach (ImageBox image in page.Images)
        {
            DrawImage(canvas, image, pictures, fonts, ct);
        }

        foreach (TextSpan span in page.Spans)
        {
            DrawSpan(canvas, span, fonts, ink);
        }
    }

    private static void DrawRoundedBox(SKCanvas canvas, RenderBox box)
    {
        SKRect bounds = RectOf(box.Bounds);
        BorderSide outline = box.Border.Top;

        if (box.Background is { IsVisible: true } background)
        {
            using var fill = new SKPaint { Color = ColorOf(background), IsAntialias = true };
            canvas.DrawRoundRect(bounds, box.Radius, box.Radius, fill);
        }

        if (!outline.IsVisible)
        {
            return;
        }

        float inset = outline.Width / 2;
        float radius = Math.Max(0, box.Radius - inset);
        var stroked = new SKRect(bounds.Left + inset, bounds.Top + inset, bounds.Right - inset, bounds.Bottom - inset);
        using var stroke = new SKPaint
        {
            Color = ColorOf(outline.Color),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = outline.Width,
            IsAntialias = true,
        };

        canvas.DrawRoundRect(stroked, radius, radius, stroke);
    }

    private static void DrawBox(SKCanvas canvas, RenderBox box)
    {
        if (box.Radius > 0)
        {
            DrawRoundedBox(canvas, box);
            return;
        }

        Rect bounds = box.Bounds;
        Border border = box.Border;

        if (box.Background is { IsVisible: true } background)
        {
            using var fill = new SKPaint { Color = ColorOf(background) };
            canvas.DrawRect(RectOf(bounds), fill);
        }

        float top = bounds.Y + (border.Top.Width / 2);
        float bottom = bounds.Bottom - (border.Bottom.Width / 2);
        float left = bounds.X + (border.Left.Width / 2);
        float right = bounds.Right - (border.Right.Width / 2);

        DrawSide(canvas, border.Top, new SKPoint(bounds.X, top), new SKPoint(bounds.Right, top));
        DrawSide(canvas, border.Bottom, new SKPoint(bounds.X, bottom), new SKPoint(bounds.Right, bottom));
        DrawSide(canvas, border.Left, new SKPoint(left, bounds.Y), new SKPoint(left, bounds.Bottom));
        DrawSide(canvas, border.Right, new SKPoint(right, bounds.Y), new SKPoint(right, bounds.Bottom));
    }

    private static void DrawSide(SKCanvas canvas, BorderSide side, SKPoint from, SKPoint to)
    {
        if (!side.IsVisible)
        {
            return;
        }

        using SKPathEffect? dash = DashOf(side);
        using var stroke = new SKPaint
        {
            Color = ColorOf(side.Color),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = side.Width,
            PathEffect = dash,
            IsAntialias = true,
        };

        canvas.DrawLine(from, to, stroke);
    }

    private static SKPathEffect? DashOf(BorderSide side) => side.Style switch
    {
        BorderStyle.Dashed => SKPathEffect.CreateDash([side.Width * 3, side.Width * 3], 0),
        BorderStyle.Dotted => SKPathEffect.CreateDash([side.Width, side.Width], 0),
        _ => null,
    };

    /// <summary>Draws a picture into its box: a drawing as shapes and text, anything else as pixels.</summary>
    /// <remarks>
    /// A drawing is never turned into pixels on its way into the file, so its lines stay sharp at any zoom and its
    /// labels are set in the document's own faces, searched and copied like the rest of the page.
    /// </remarks>
    private static void DrawImage(SKCanvas canvas, ImageBox box, Decoded pictures, Faces fonts, CancellationToken ct)
    {
        if (box.Image.IsVector)
        {
            Drawings.Draw(canvas, box.Image, RectOf(box.Bounds), fonts, box.Source, ct);
            return;
        }

        if (pictures.Of(box.Image) is not { } picture)
        {
            return;
        }

        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

        if (box.Source is { } source)
        {
            canvas.DrawImage(picture, RectOf(source), RectOf(box.Bounds), sampling, null);
            return;
        }

        canvas.DrawImage(picture, RectOf(box.Bounds), sampling, null);
    }

    /// <summary>Draws a run in its colour, and the rules its decoration asks for.</summary>
    /// <remarks>
    /// One paint serves every run of the document, given each run's colour in turn: a paint made and released
    /// for every run, and the face's metrics read for every run whether it had a rule to draw or not, were
    /// native calls by the thousand for a long document, and the file records the colour of a run, not which
    /// paint carried it.
    /// </remarks>
    private static void DrawSpan(SKCanvas canvas, TextSpan span, Faces fonts, SKPaint ink)
    {
        SKFont font = fonts.For(span.Font);

        ink.Color = ColorOf(span.Color);

        if (span.Font.Tracking == 0)
        {
            canvas.DrawText(span.Text, span.X, span.Baseline, SKTextAlign.Left, font, ink);
        }
        else
        {
            DrawTracked(canvas, span, font, ink);
        }

        if (!span.Decoration.HasFlag(TextDecoration.Underline) && !span.Decoration.HasFlag(TextDecoration.LineThrough))
        {
            return;
        }

        SKFontMetrics metrics = font.Metrics;
        float thickness = metrics.UnderlineThickness ?? (span.Font.Size / 16);

        if (span.Decoration.HasFlag(TextDecoration.Underline))
        {
            DrawRule(canvas, span, metrics.UnderlinePosition ?? (span.Font.Size / 10), thickness, ink);
        }

        if (span.Decoration.HasFlag(TextDecoration.LineThrough))
        {
            DrawRule(canvas, span, metrics.StrikeoutPosition ?? (-span.Font.Size * 0.3f), metrics.StrikeoutThickness ?? thickness, ink);
        }
    }

    /// <summary>Draws a tracked run glyph by glyph, at the very positions its width was measured from.</summary>
    /// <remarks>
    /// The canvas has no letter spacing of its own, so each glyph is placed by hand. The advances come from the same
    /// font object the line was measured with, and the run stays one text object in the file, so the text is
    /// still searched and copied as a whole.
    /// </remarks>
    private static void DrawTracked(SKCanvas canvas, TextSpan span, SKFont font, SKPaint ink)
    {
        ushort[] glyphs = font.GetGlyphs(span.Text);
        float[] widths = font.GetGlyphWidths(glyphs);
        float[] positions = new float[glyphs.Length];
        float pen = span.X;

        for (int index = 0; index < glyphs.Length; index++)
        {
            positions[index] = pen;
            pen += widths[index] + span.Font.Tracking;
        }

        using var run = SKTextBlob.CreateHorizontal(span.Text, font, positions, span.Baseline);

        canvas.DrawText(run, 0, 0, ink);
    }

    private static void DrawRule(SKCanvas canvas, TextSpan span, float offset, float thickness, SKPaint ink)
    {
        float top = span.Baseline + offset;

        canvas.DrawRect(new SKRect(span.X, top, span.X + span.Width, top + thickness), ink);
    }

    private static SKColor ColorOf(Rgba color) => new(color.R, color.G, color.B, color.A);

    private static SKRect RectOf(Rect rect) => new(rect.X, rect.Y, rect.Right, rect.Bottom);

    /// <summary>Every picture of one document, decoded on first use and released with the document.</summary>
    private sealed class Decoded : IDisposable
    {
        private readonly Dictionary<Image, SKImage?> _images = new(ReferenceEqualityComparer.Instance);

        public SKImage? Of(Image image)
        {
            if (!_images.TryGetValue(image, out SKImage? decoded))
            {
                decoded = SKImage.FromEncodedData(image.Content);
                _images[image] = decoded;
            }

            return decoded;
        }

        public void Dispose()
        {
            foreach (SKImage? image in _images.Values)
            {
                image?.Dispose();
            }

            _images.Clear();
        }
    }
}
