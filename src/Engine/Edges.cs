namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>Four lengths in points, one for each side of a box.</summary>
/// <param name="Top">The length along the top side.</param>
/// <param name="Right">The length along the right side.</param>
/// <param name="Bottom">The length along the bottom side.</param>
/// <param name="Left">The length along the left side.</param>
public readonly record struct Edges(float Top, float Right, float Bottom, float Left)
{
    /// <summary>The same length on every side.</summary>
    public static Edges All(float length) => new(length, length, length, length);

    /// <summary>The left and right lengths together.</summary>
    public float Horizontal => Left + Right;

    /// <summary>The top and bottom lengths together.</summary>
    public float Vertical => Top + Bottom;
}
