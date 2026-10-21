using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>A picture as the engine needs it: its bytes and its natural size, or nothing when the bytes are not a picture.</summary>
/// <remarks>
/// The size comes from the header alone; the pixels are decoded by the renderer, once, when it draws. Every
/// picture source answers through this so that what counts as a picture is decided in one place. SVG markup
/// is a picture too: it is sized from its root and drawn as shapes by the renderer, never decoded to pixels.
/// </remarks>
internal static class Decoding
{
    /// <summary>The most pixels a raster picture may hold: 50 million, 200 MB once decoded.</summary>
    /// <remarks>
    /// The header says how large a picture is, and the renderer decodes it whole: a PNG of 1.1 MB declaring
    /// 20 000 × 20 000 pixels took 3.26 GB to draw a hundred points wide, and one larger ended the process for
    /// want of memory. Fifty million pixels is the largest camera a document is likely to be handed, with room
    /// to spare; a picture past it is left out and noted as unreadable, as any picture that cannot be drawn is.
    /// </remarks>
    internal const long MostPixels = 50_000_000;

    public static Image? Read(byte[] content)
    {
        if (Svg.IsMarkup(content))
        {
            return Svg.Read(content);
        }

        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);

        if (codec is null || (long)codec.Info.Width * codec.Info.Height > MostPixels)
        {
            return null;
        }

        return new Image(content, codec.Info.Width, codec.Info.Height);
    }
}
