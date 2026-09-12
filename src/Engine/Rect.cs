namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>An axis-aligned rectangle in points, measured from the top-left corner of the page.</summary>
/// <param name="X">Distance from the left edge of the page.</param>
/// <param name="Y">Distance from the top edge of the page.</param>
/// <param name="Width">Extent to the right.</param>
/// <param name="Height">Extent downwards.</param>
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    /// <summary>Distance of the right side from the left edge of the page.</summary>
    public float Right => X + Width;

    /// <summary>Distance of the bottom side from the top edge of the page.</summary>
    public float Bottom => Y + Height;
}
