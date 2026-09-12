using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>A picture as the engine needs it: its bytes and its natural size, or nothing when the bytes are not a picture.</summary>
/// <remarks>
/// The size comes from the header alone; the pixels are decoded by the renderer, once, when it draws. Every
/// picture source answers through this so that what counts as a picture is decided in one place.
/// </remarks>
internal static class Decoding
{
    public static Image? Read(byte[] content)
    {
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);

        return codec is null ? null : new Image(content, codec.Info.Width, codec.Info.Height);
    }
}
