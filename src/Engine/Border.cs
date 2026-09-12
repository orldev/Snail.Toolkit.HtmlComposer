namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>How a border side is stroked.</summary>
public enum BorderStyle
{
    /// <summary>A continuous line.</summary>
    Solid,

    /// <summary>A line of dashes.</summary>
    Dashed,

    /// <summary>A line of dots.</summary>
    Dotted,
}

/// <summary>One side of a border.</summary>
/// <param name="Width">Thickness in points.</param>
/// <param name="Color">The stroke colour.</param>
/// <param name="Style">How the side is stroked.</param>
public readonly record struct BorderSide(float Width, Rgba Color, BorderStyle Style = BorderStyle.Solid)
{
    /// <summary>Whether anything shows when this side is drawn.</summary>
    public bool IsVisible => Width > 0 && Color.IsVisible;
}

/// <summary>The four sides of a box's border.</summary>
/// <param name="Top">The top side.</param>
/// <param name="Right">The right side.</param>
/// <param name="Bottom">The bottom side.</param>
/// <param name="Left">The left side.</param>
public readonly record struct Border(BorderSide Top, BorderSide Right, BorderSide Bottom, BorderSide Left)
{
    /// <summary>A border with no side drawn.</summary>
    public static Border None => default;

    /// <summary>Whether any side shows.</summary>
    public bool IsVisible => Top.IsVisible || Right.IsVisible || Bottom.IsVisible || Left.IsVisible;

    /// <summary>The thickness of each side.</summary>
    public Edges Widths => new(Top.Width, Right.Width, Bottom.Width, Left.Width);
}
