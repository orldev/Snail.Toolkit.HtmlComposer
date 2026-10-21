namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>What the running header, footer and side note put on one page.</summary>
/// <param name="Boxes">Their boxes; the same objects as in <see cref="Page.Boxes"/>.</param>
/// <param name="Images">Their pictures; the same objects as in <see cref="Page.Images"/>.</param>
/// <param name="Spans">Their runs; the same objects as in <see cref="Page.Spans"/>.</param>
/// <remarks>
/// The markup of a running line is the same on every page of a section and only its numbers differ, which a
/// run marks with <see cref="TextSpan.Field"/>. A renderer that has a place for what repeats — a deck's slide
/// layout — compares these across pages and keeps one copy.
/// </remarks>
public sealed record Running(IReadOnlyList<RenderBox> Boxes, IReadOnlyList<ImageBox> Images, IReadOnlyList<TextSpan> Spans)
{
    /// <summary>The blocks of text of the running lines as paragraphs.</summary>
    public IReadOnlyList<TextFrame> Texts { get; init; } = [];

    /// <summary>The tables of the running lines as grids.</summary>
    public IReadOnlyList<TableFrame> Tables { get; init; } = [];
}
