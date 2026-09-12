namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>A picture scaled into its place on a page.</summary>
/// <param name="Bounds">The rectangle the picture fills.</param>
/// <param name="Image">The picture.</param>
/// <param name="Source">
/// The part of the picture to draw, in its own pixels; the whole picture when absent.
/// </param>
/// <remarks>
/// A source rectangle is how <c>object-fit: cover</c> reaches the renderer: the engine settles which part of
/// the picture is seen, and the renderer only draws that part into the bounds. Cropping in the renderer
/// instead would mean it had to know the fitting rules, which are a matter of layout.
/// </remarks>
public sealed record ImageBox(Rect Bounds, Image Image, Rect? Source = null);
