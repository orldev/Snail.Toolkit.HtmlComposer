namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>One finished page, everything on it positioned in points from its top-left corner.</summary>
/// <param name="Number">The page number, starting at 1.</param>
/// <param name="Width">Page width.</param>
/// <param name="Height">Page height.</param>
/// <param name="Boxes">Element boxes with their backgrounds and borders.</param>
/// <param name="Images">Pictures scaled into place.</param>
/// <param name="Spans">Runs of text.</param>
/// <remarks>
/// The lists are in paint order: boxes first, then images, then text. That is the order CSS paints normal flow
/// in, with the backgrounds and borders of blocks beneath their inline content, so a renderer that draws them
/// list by list gets the layering right without sorting anything.
/// </remarks>
public sealed record Page(
    int Number,
    float Width,
    float Height,
    IReadOnlyList<RenderBox> Boxes,
    IReadOnlyList<ImageBox> Images,
    IReadOnlyList<TextSpan> Spans);
