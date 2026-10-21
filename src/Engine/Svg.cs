using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>Tells SVG markup from an encoded raster picture, and the natural size the markup asks for.</summary>
/// <remarks>
/// The engine needs only the size to reserve room; drawing the shapes is the renderer's part, so nothing here
/// looks below the root element. A size is read the way a browser reads the root of an SVG document: the
/// <c>width</c> and <c>height</c> attributes when they are absolute lengths, the proportions of the
/// <c>viewBox</c> for the side that is missing, and 300 × 150 when neither says anything. A drawing with only a
/// <c>viewBox</c> takes the box itself as its size in CSS pixels rather than a browser's 300 pixels wide: a
/// document has no window to stretch it across, and the box is what the drawing was made in.
/// </remarks>
public static class Svg
{
    private const float DefaultWidth = 300;

    private const float DefaultHeight = 150;

    private const int Sniffed = 1024;

    private static readonly char[] Separators = [' ', ',', '\t', '\r', '\n'];

    /// <summary>Whether the bytes are SVG markup rather than an encoded picture.</summary>
    /// <remarks>
    /// Every raster format the renderers read starts with a binary signature, and none of them with <c>&lt;</c>,
    /// so markup is recognised by its first character and an <c>svg</c> tag within its first kilobyte — past a
    /// byte order mark, an XML declaration, comments or a doctype.
    /// </remarks>
    public static bool IsMarkup(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> text = content.StartsWith("﻿"u8) ? content[3..] : content;
        int start = text.IndexOfAnyExcept(" \t\r\n"u8);

        if (start < 0 || text[start] != (byte)'<')
        {
            return false;
        }

        ReadOnlySpan<byte> head = text[start..Math.Min(text.Length, start + Sniffed)];

        return head.IndexOf("<svg"u8) >= 0 || head.IndexOf("<SVG"u8) >= 0;
    }

    /// <summary>The picture SVG markup makes, sized as its root asks, or nothing when the bytes hold no drawing.</summary>
    public static Image? Read(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!IsMarkup(content))
        {
            return null;
        }

        IElement? root = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(content)).QuerySelector("svg");

        return root is null ? null : Sized(content, root);
    }

    /// <summary>The picture an <c>svg</c> element written straight into the document makes.</summary>
    internal static Image Of(IElement element) => Sized(Encoding.UTF8.GetBytes(element.OuterHtml), element);

    private static Image Sized(byte[] content, IElement root)
    {
        (float width, float height) = SizeOf(root);

        return new Image(content, Whole(width), Whole(height)) { IsVector = true };
    }

    private static int Whole(float pixels) => Math.Max(1, (int)MathF.Round(pixels));

    private static (float Width, float Height) SizeOf(IElement root)
    {
        float? width = PixelsOf(root.GetAttribute("width"));
        float? height = PixelsOf(root.GetAttribute("height"));
        (float Width, float Height)? box = BoxOf(root.GetAttribute("viewBox") ?? root.GetAttribute("viewbox"));

        return (width, height, box) switch
        {
            ({ } across, { } down, _) => (across, down),
            ({ } across, null, { } drawn) => (across, across * drawn.Height / drawn.Width),
            (null, { } down, { } drawn) => (down * drawn.Width / drawn.Height, down),
            (null, null, { } drawn) => drawn,
            ({ } across, null, null) => (across, DefaultHeight),
            (null, { } down, null) => (DefaultWidth, down),
            _ => (DefaultWidth, DefaultHeight),
        };
    }

    /// <summary>The width and height of a <c>viewBox</c>, or nothing when it is absent or degenerate.</summary>
    internal static (float Width, float Height)? BoxOf(string? viewBox)
    {
        string[] parts = viewBox?.Split(Separators, StringSplitOptions.RemoveEmptyEntries) ?? [];

        if (parts.Length != 4
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float width)
            || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float height)
            || width <= 0
            || height <= 0)
        {
            return null;
        }

        return (width, height);
    }

    /// <summary>An absolute SVG length in CSS pixels, or nothing for a percentage, a relative unit or no length.</summary>
    internal static float? PixelsOf(string? length)
    {
        if (string.IsNullOrWhiteSpace(length))
        {
            return null;
        }

        string trimmed = length.Trim();
        int end = trimmed.Length;

        while (end > 0 && char.IsAsciiLetter(trimmed[end - 1]))
        {
            end--;
        }

        if (!float.TryParse(trimmed[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out float number) || number <= 0)
        {
            return null;
        }

        return trimmed[end..].ToLowerInvariant() switch
        {
            "" or "px" => number,
            "pt" => number / Points.PerPixel,
            "pc" => number * 16,
            "in" => number * 96,
            "cm" => number * 96 / 2.54f,
            "mm" => number * 96 / 25.4f,
            _ => null,
        };
    }
}
