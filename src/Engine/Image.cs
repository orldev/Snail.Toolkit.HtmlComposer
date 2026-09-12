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
public sealed record Image(byte[] Content, int Width, int Height);
