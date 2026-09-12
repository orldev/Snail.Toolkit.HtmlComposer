namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>A colour with straight, not premultiplied, alpha.</summary>
/// <param name="R">Red, 0 to 255.</param>
/// <param name="G">Green, 0 to 255.</param>
/// <param name="B">Blue, 0 to 255.</param>
/// <param name="A">Opacity, 0 for fully transparent to 255 for opaque.</param>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Opaque black, the initial text colour.</summary>
    public static Rgba Black { get; } = new(0, 0, 0);

    /// <summary>Whether anything shows when this colour is painted.</summary>
    public bool IsVisible => A > 0;
}
