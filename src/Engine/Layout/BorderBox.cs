using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>Where a block's border box stands and how wide it is, once its margins and sizing are settled.</summary>
/// <param name="X">The left edge of the border box.</param>
/// <param name="Width">The width of the border box.</param>
/// <param name="Padding">The padding, resolved against the containing block.</param>
/// <param name="Borders">The thickness of each border side.</param>
/// <remarks>
/// A block and a table are placed by the same rules and differ only in what they hold, so the placing is
/// settled here once and both take their content area from it.
/// </remarks>
internal readonly record struct BorderBox(float X, float Width, Edges Padding, Edges Borders)
{
    /// <summary>What the padding and the borders take from the width, together.</summary>
    public float Chrome => Padding.Horizontal + Borders.Horizontal;

    /// <summary>The left edge of the content area.</summary>
    public float ContentLeft => X + Borders.Left + Padding.Left;

    /// <summary>The width of the content area.</summary>
    public float ContentWidth => Width - Chrome;

    /// <summary>Places a block of the given style inside its containing block.</summary>
    /// <remarks>
    /// A declared width is the content width unless <c>box-sizing: border-box</c> says it is the whole box,
    /// and a block without one fills what its margins leave. The width never drops below the chrome, so a
    /// block asked to be narrower than its own padding still paints that padding.
    /// </remarks>
    public static BorderBox Of(Style style, float left, float containingWidth)
    {
        Edges padding = style.Padding.Resolve(containingWidth);
        Edges borders = style.Border.Widths;
        float width = WidthOf(style, containingWidth, padding.Horizontal + borders.Horizontal);

        return new BorderBox(left + LeftMargin(style.Margin, containingWidth, width), width, padding, borders);
    }

    private static float WidthOf(Style style, float containingWidth, float chrome)
    {
        float sizing = style.IsBorderBox ? 0 : chrome;
        float margins = (style.Margin.Left?.Resolve(containingWidth) ?? 0) + (style.Margin.Right?.Resolve(containingWidth) ?? 0);
        float width = style.Width is { } declared ? declared.Resolve(containingWidth) + sizing : containingWidth - margins;
        float limit = style.MaxWidth is { } maximum ? maximum.Resolve(containingWidth) + sizing : float.MaxValue;

        return Math.Max(chrome, Math.Min(width, limit));
    }

    /// <summary>How far from the containing block's left edge the box starts, with <c>auto</c> margins centring it.</summary>
    private static float LeftMargin(Sides margin, float containingWidth, float width)
    {
        float free = containingWidth - width;

        return (margin.Left, margin.Right) switch
        {
            (null, null) => free / 2,
            (null, { } right) => free - right.Resolve(containingWidth),
            ({ } left, _) => left.Resolve(containingWidth),
        };
    }
}
