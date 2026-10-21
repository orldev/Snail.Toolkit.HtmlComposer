namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>Where the content of a table cell sits between its top and bottom.</summary>
public enum CellAlignment
{
    /// <summary>At the top, which is also where a cell aligned on the baseline is given back.</summary>
    Top,

    /// <summary>In the middle.</summary>
    Middle,

    /// <summary>At the foot.</summary>
    Bottom,
}

/// <summary>One cell of a table as it was set on a page.</summary>
/// <param name="Column">The first column the cell covers, counted from zero.</param>
/// <param name="Span">How many columns the cell covers.</param>
/// <param name="Bounds">The cell on the page, as tall as its row.</param>
/// <param name="Padding">
/// How far the content stands in from each side of the cell. For a cell aligned at the top this is measured to
/// where its first line really begins, so a cell lowered onto the baseline of its neighbours keeps its place.
/// </param>
/// <param name="Alignment">Where the content sits between top and bottom.</param>
/// <param name="Top">The rule along the top edge, or a side of width zero where none is drawn.</param>
/// <param name="Bottom">The rule along the bottom edge, or a side of width zero where none is drawn.</param>
/// <param name="Background">The fill of the cell, or of its row; <see langword="null"/> when none is painted.</param>
/// <param name="Paragraphs">The text of the cell as paragraphs.</param>
/// <param name="Picture">The one picture a cell holds without any text, which a renderer may paint as the cell's fill.</param>
/// <remarks>
/// The rules are resolved per edge: where one row meets the next, the heavier of what the two declared is the
/// rule, and both cells report that same rule — one as its bottom, the other as its top. A renderer that draws
/// each cell's sides on its own then draws one rule in one weight, which is what a deck needs: its tables keep
/// one line per edge and leave the choice between two disagreeing cells to whoever reads them.
/// </remarks>
public sealed record TableCell(
    int Column,
    int Span,
    Rect Bounds,
    Edges Padding,
    CellAlignment Alignment,
    BorderSide Top,
    BorderSide Bottom,
    Rgba? Background,
    IReadOnlyList<TextParagraph> Paragraphs,
    ImageBox? Picture);

/// <summary>One row of a table on a page.</summary>
/// <param name="Top">The top of the row on the page.</param>
/// <param name="Height">The height of the row.</param>
/// <param name="IsHeader">Whether the row heads the table and is repeated on every page it reaches.</param>
/// <param name="Cells">The cells from left to right.</param>
public sealed record TableRow(float Top, float Height, bool IsHeader, IReadOnlyList<TableCell> Cells);

/// <summary>The part of a table that landed on one page, as columns, rows and cells.</summary>
/// <param name="Bounds">The grid on the page: from the left of its first column to the right of its last.</param>
/// <param name="Columns">The width of every column, left to right.</param>
/// <param name="Rows">The rows on this page, the repeated heading first.</param>
/// <param name="Boxes">The boxes of the table, its rows and its cells; the same objects as in <see cref="Page.Boxes"/>.</param>
/// <remarks>
/// A table that crosses pages gives one frame per page, each opening with the heading again, which is how a
/// deck carries a table on: a new table on the next slide with the same head. A table is given as a grid only
/// when it is one — a cell spanning rows, or a table inside a cell, has no place in a grid of simple rows, and
/// such a table keeps its boxes and runs placed one by one.
/// </remarks>
public sealed record TableFrame(
    Rect Bounds,
    IReadOnlyList<float> Columns,
    IReadOnlyList<TableRow> Rows,
    IReadOnlyList<RenderBox> Boxes)
{
    /// <summary>What the cells hold beyond their text: the boxes of blocks inside them, markers, painted inline runs.</summary>
    /// <remarks>
    /// Drawn one by one, these lie on top of the cells' fills, as they do on the page. A renderer that draws the
    /// table as one object of its own — a deck — has to paint them after it, or a coloured cell hides them.
    /// </remarks>
    public IReadOnlyList<RenderBox> CellBoxes { get; init; } = [];

    /// <summary>The pictures the cells hold beside their text, which a renderer drawing the table as one object paints after it.</summary>
    public IReadOnlyList<ImageBox> CellImages { get; init; } = [];
}
