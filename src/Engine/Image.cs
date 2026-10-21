namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>An encoded picture with its natural size.</summary>
/// <param name="Content">The encoded bytes, in whatever format the renderer decodes.</param>
/// <param name="Width">Natural width in CSS pixels.</param>
/// <param name="Height">Natural height in CSS pixels.</param>
/// <remarks>
/// The bytes travel undecoded from the source to the renderer. The engine only needs the natural size to
/// reserve room, and holding decoded pixels for the whole document would cost the memory of every picture
/// at full resolution before a single page is drawn.
/// </remarks>
public sealed record Image(byte[] Content, int Width, int Height)
{
    /// <summary>Whether the bytes are SVG markup to be drawn as shapes rather than pixels to be decoded.</summary>
    /// <remarks>
    /// The engine sizes both alike and never looks inside; the flag tells a renderer which way to draw, so a PDF
    /// keeps the lines of a drawing as lines and its words as text that is searched and copied.
    /// </remarks>
    public bool IsVector { get; init; }
}
