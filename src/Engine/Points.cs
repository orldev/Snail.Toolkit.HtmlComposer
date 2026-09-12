namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>Converts physical lengths to points, the unit every coordinate in a composition is written in.</summary>
/// <remarks>
/// A point is 1/72 inch, the unit of PDF user space, so a renderer draws coordinates as they come. A CSS pixel
/// is 1/96 inch, which makes one pixel exactly three quarters of a point.
/// </remarks>
public static class Points
{
    /// <summary>Points in one inch.</summary>
    public const float PerInch = 72f;

    /// <summary>Points in one CSS pixel.</summary>
    public const float PerPixel = 0.75f;

    /// <summary>The length of the given number of millimetres.</summary>
    public static float FromMillimeters(float millimeters) => millimeters * PerInch / 25.4f;

    /// <summary>The length of the given number of inches.</summary>
    public static float FromInches(float inches) => inches * PerInch;

    /// <summary>The length of the given number of CSS pixels.</summary>
    public static float FromPixels(float pixels) => pixels * PerPixel;
}
