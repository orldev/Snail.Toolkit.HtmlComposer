using System.Text.RegularExpressions;
using AngleSharp.Dom;
using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>Sets one SVG <c>text</c> element, with its <c>tspan</c> pieces, in the faces of the document.</summary>
/// <remarks>
/// Text is drawn as text, not as outlines: the same <see cref="Faces"/> that set the page set the label, so a
/// label in a PDF is searched and copied, and a family that is not loaded falls back exactly as it does in
/// the page. White space collapses as SVG's default asks — runs of spaces and line ends become one space, and
/// the text is trimmed at both ends. An anchor other than <c>start</c> shifts the whole element by its width.
/// </remarks>
internal sealed partial class Lettering(SKCanvas canvas, Faces faces)
{
    public void Draw(IElement text, Presentation ink, Area area, float fade)
    {
        List<Piece> pieces = [];

        Gather(text, ink, area, pieces, 0);

        List<Piece> collapsed = Collapsed(pieces);

        if (collapsed.Count == 0)
        {
            return;
        }

        float x = Numbers.Of(text.GetAttribute("x")).FirstOrDefault() + Numbers.Of(text.GetAttribute("dx")).FirstOrDefault();
        float y = Numbers.Of(text.GetAttribute("y")).FirstOrDefault() + Numbers.Of(text.GetAttribute("dy")).FirstOrDefault();
        float width = collapsed.Sum(piece => faces.Measure(piece.Text, FontOf(piece.Ink)));
        float pen = ink.Anchor switch
        {
            TextAnchor.Middle => x - (width / 2),
            TextAnchor.End => x - width,
            _ => x,
        };

        foreach (Piece piece in collapsed)
        {
            pen = (piece.X ?? pen) + piece.Dx;
            y = (piece.Y ?? y) + piece.Dy;
            pen += Set(piece, pen, y, fade);
        }
    }

    private static void Gather(IElement element, Presentation ink, Area area, List<Piece> pieces, int depth)
    {
        if (depth >= Sketch.Deepest)
        {
            return;
        }

        INodeList children = element.ChildNodes;

        for (int index = 0; index < children.Length; index++)
        {
            INode child = children[index];

            if (child is IText text)
            {
                pieces.Add(new Piece(text.Data, ink));
                continue;
            }

            if (child is not IElement span || !span.LocalName.Equals("tspan", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Properties properties = Properties.Of(span);

            if (properties.Has("display", "none"))
            {
                continue;
            }

            int first = pieces.Count;

            Gather(span, ink.Inherit(properties, area), area, pieces, depth + 1);

            if (pieces.Count > first)
            {
                pieces[first] = Placed(pieces[first], span);
            }
        }
    }

    private static Piece Placed(Piece piece, IElement span) => piece with
    {
        X = Numbers.Of(span.GetAttribute("x")) is [var x, ..] ? x : null,
        Y = Numbers.Of(span.GetAttribute("y")) is [var y, ..] ? y : null,
        Dx = Numbers.Of(span.GetAttribute("dx")).FirstOrDefault(),
        Dy = Numbers.Of(span.GetAttribute("dy")).FirstOrDefault(),
    };

    private static List<Piece> Collapsed(List<Piece> pieces)
    {
        List<Piece> collapsed = [];
        bool isAfterSpace = true;

        foreach (Piece piece in pieces)
        {
            string text = Spaces().Replace(piece.Text, " ");

            if (isAfterSpace)
            {
                text = text.TrimStart();
            }

            if (text.Length == 0)
            {
                continue;
            }

            isAfterSpace = text.EndsWith(' ');
            collapsed.Add(piece with { Text = text });
        }

        if (collapsed is [.., var last])
        {
            collapsed[^1] = last with { Text = last.Text.TrimEnd() };
        }

        return collapsed;
    }

    private float Set(Piece piece, float x, float y, float fade)
    {
        Font font = FontOf(piece.Ink);
        float width = faces.Measure(piece.Text, font);

        if (!piece.Ink.IsVisible || piece.Ink.Fill is not { } color || piece.Text.Length == 0)
        {
            return width;
        }

        SKFont face = faces.For(font);
        using var ink = new SKPaint { IsAntialias = true, Color = Presentation.Faded(color, piece.Ink.FillOpacity * fade) };

        if (font.Tracking == 0)
        {
            canvas.DrawText(piece.Text, x, y, SKTextAlign.Left, face, ink);

            return width;
        }

        ushort[] glyphs = face.GetGlyphs(piece.Text);
        float[] advances = face.GetGlyphWidths(glyphs);
        float[] positions = new float[glyphs.Length];
        float pen = x;

        for (int index = 0; index < glyphs.Length; index++)
        {
            positions[index] = pen;
            pen += advances[index] + font.Tracking;
        }

        using var run = SKTextBlob.CreateHorizontal(piece.Text, face, positions, y);
        canvas.DrawText(run, 0, 0, ink);

        return width;
    }

    private static Font FontOf(Presentation ink) => new(ink.Family, ink.Size, ink.Weight, ink.IsItalic, ink.Spacing);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    /// <summary>A run of text with the presentation it is drawn in, and where its <c>tspan</c> moved it.</summary>
    private sealed record Piece(string Text, Presentation Ink, float? X = null, float? Y = null, float Dx = 0, float Dy = 0);
}
