using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>Draws SVG pictures onto a Skia canvas as shapes and text, at whatever size the frame asks.</summary>
/// <remarks>
/// Every renderer draws through here, so a drawing looks the same in a PDF and in a deck. On a PDF canvas the
/// shapes stay vectors and the labels stay text, set in the very faces the document was set in: a word inside a
/// drawing is searched and copied like any other. A format that cannot carry shapes takes the pixels of
/// <see cref="Rasterized"/> instead.
/// <para>
/// The part of SVG drawn is the part documents are made of: <c>rect</c>, <c>circle</c>, <c>ellipse</c>,
/// <c>line</c>, <c>polyline</c>, <c>polygon</c>, <c>path</c>, <c>text</c> with <c>tspan</c>, <c>g</c>,
/// nested <c>svg</c> and <c>use</c>; fills, strokes, dashes, opacity, transforms, <c>viewBox</c> with
/// <c>preserveAspectRatio</c>, presentation attributes and the <c>style</c> attribute. Gradients, patterns,
/// masks, clip paths, filters, embedded pictures and <c>&lt;style&gt;</c> sheets are not drawn: a paint
/// that names a gradient falls back to the colour written after it, or to none.
/// </para>
/// </remarks>
public static class Drawings
{
    /// <summary>Draws a vector picture into a frame of the canvas, or only the part a crop names.</summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="image">The picture; anything but SVG markup is left alone.</param>
    /// <param name="frame">Where the picture lands, in the canvas's units.</param>
    /// <param name="faces">The faces the labels of the drawing are set in.</param>
    /// <param name="source">The part of the picture to show, in its natural pixels; the whole picture when absent.</param>
    /// <param name="ct">Looked at while the drawing is walked, so one drawing too large to finish stops when its caller gives up.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled while the drawing was drawn.</exception>
    public static void Draw(SKCanvas canvas, Image image, SKRect frame, Faces faces, Rect? source = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(faces);

        if (!image.IsVector || RootOf(image.Content) is not { } root)
        {
            return;
        }

        SKRect shown = source is { } crop
            ? new SKRect(crop.X, crop.Y, crop.Right, crop.Bottom)
            : new SKRect(0, 0, image.Width, image.Height);
        int saved = canvas.Save();

        canvas.ClipRect(frame);
        canvas.Translate(frame.Left, frame.Top);
        canvas.Scale(frame.Width / shown.Width, frame.Height / shown.Height);
        canvas.Translate(-shown.Left, -shown.Top);
        new Sketch(canvas, faces, ct).Root(root, image.Width, image.Height);
        canvas.RestoreToCount(saved);
    }

    /// <summary>The picture drawn into PNG pixels of the given size, on a transparent ground.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Skia cannot make a surface of that size.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled while the drawing was drawn.</exception>
    public static byte[] Rasterized(Image image, int width, int height, Faces faces, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var info = new SKImageInfo(Math.Max(1, width), Math.Max(1, height), SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface? surface = SKSurface.Create(info);

        if (surface is null)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"A surface of {info.Width} × {info.Height} pixels cannot be made.");
        }

        surface.Canvas.Clear(SKColors.Transparent);
        Draw(surface.Canvas, image, new SKRect(0, 0, info.Width, info.Height), faces, ct: ct);

        using SKImage snapshot = surface.Snapshot();
        using SKData png = snapshot.Encode(SKEncodedImageFormat.Png, 100);

        return png.ToArray();
    }

    /// <summary>Whether a drawing holds more than the budget of one drawing, so that part of it will not be drawn.</summary>
    internal static bool IsOverBudget(Image image) => image.IsVector && RootOf(image.Content) is { } root && Sketch.IsOverBudget(root);

    private static IElement? RootOf(byte[] content) =>
        new HtmlParser().ParseDocument(Encoding.UTF8.GetString(content)).QuerySelector("svg");
}
