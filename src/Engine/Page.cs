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
    IReadOnlyList<TextSpan> Spans)
{
    /// <summary>The blocks of text on the page as paragraphs, for a renderer whose text flows again on its own.</summary>
    /// <remarks>Every run named here is also in <see cref="Spans"/>; a renderer that draws positions alone can ignore this.</remarks>
    public IReadOnlyList<TextFrame> Texts { get; init; } = [];

    /// <summary>The tables on the page as grids, for a renderer that has tables of its own.</summary>
    /// <remarks>Every box, run and picture named here is also in the lists above; a renderer that draws positions alone can ignore this.</remarks>
    public IReadOnlyList<TableFrame> Tables { get; init; } = [];

    /// <summary>What the running header, footer and side note put on this page; <see langword="null"/> when there are none.</summary>
    /// <remarks>
    /// Running lines are drawn with the page — their boxes, pictures and runs are in the lists above — and are
    /// also told apart here, because a deck keeps them once, on the layout every slide is made from, instead of
    /// on each slide.
    /// </remarks>
    public Running? Running { get; init; }
}
