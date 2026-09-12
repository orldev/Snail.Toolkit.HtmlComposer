namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>A box on one page, with the background and border it paints there.</summary>
/// <param name="Kind">What the box stands for: the sheet, a list marker, the box of a run, or an element's box.</param>
/// <param name="Bounds">The border box on this page.</param>
/// <param name="Background">The background colour, or <see langword="null"/> when none is painted.</param>
/// <param name="Border">The border sides drawn on this page.</param>
/// <param name="IsSplitAbove">Whether the element began on an earlier page, so its top border is not drawn here.</param>
/// <param name="IsSplitBelow">Whether the element goes on to a later page, so its bottom border is not drawn here.</param>
/// <param name="Radius">The radius of every corner; 0 for square corners.</param>
/// <param name="Element">The tag name of the element the box belongs to; <see langword="null"/> for a box that is no element's.</param>
/// <remarks>
/// A block that crosses a page boundary becomes one box per page rather than one box clipped twice. Each box
/// then carries exactly the decoration visible on its page, which is CSS <c>box-decoration-break: slice</c>,
/// and a renderer draws every box the same way without knowing it was split. A rounded box is stroked with its
/// top border all the way round: CSS lets each side have its own width and colour, which rounded corners would
/// have to blend, and documents practically never ask for that.
/// </remarks>
public sealed record RenderBox(
    BoxKind Kind,
    Rect Bounds,
    Rgba? Background,
    Border Border,
    bool IsSplitAbove = false,
    bool IsSplitBelow = false,
    float Radius = 0,
    string? Element = null)
{
    /// <summary>Whether the box paints anything at all.</summary>
    public bool IsPainted => Background is { IsVisible: true } || Border.IsVisible;
}
