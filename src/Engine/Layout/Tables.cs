using System.Globalization;
using AngleSharp.Dom;
using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>Sets tables: columns of a settled width, rows as tall as their tallest cell.</summary>
/// <remarks>
/// A table is an ordinary block whose children are rows, so pagination cuts it between rows with the
/// machinery every other block already uses. A column keeps the width written on any of its cells; the rest
/// are measured from what they hold and share what is left, which is why every cell is measured before it is
/// set. The galley sets the cells and measures their content; this decides only where each one goes and how
/// tall its row comes to.
/// </remarks>
internal sealed class Tables(Galley galley)
{
    /// <summary>The most columns or rows one cell may cover, where HTML itself caps <c>colspan</c>.</summary>
    private const int MaxSpan = 1000;

    public BlockFlow Set(IElement element, Style style, float left, float containingWidth, PendingMarker? pending)
    {
        var box = BorderBox.Of(style, left, containingWidth);
        List<DeclaredRow> rows = Rows(element, style);
        List<Band> grid = Grid(rows);
        float[] columns = Columns(grid, box.ContentWidth);
        List<Flow> set = Tabulate(rows, grid, columns, box.ContentLeft, box.ContentWidth, pending);
        int held = rows.Count(row => row.IsContinued);
        List<Flow> children = [.. set.Take(set.Count - held)];

        BlockFlow table = Galley.Framed(element, style, box, children, containingWidth);

        return held == 0 ? table : table.Continuing([.. set.Skip(set.Count - held)]);
    }

    /// <summary>The width a table asks for when it is measured rather than set: its columns end to end.</summary>
    public float Width(IElement element, Style style, float basis) =>
        Columns(Grid(Rows(element, style)), basis).Sum();

    /// <summary>Sets every row of a table, once each row knows how tall the cells reaching into it have made it.</summary>
    /// <remarks>
    /// Three passes, because a cell spanning rows cannot be placed until the rows it covers are measured: the
    /// cells are set first, then each row takes the height of the tallest cell that belongs to it alone, then a
    /// spanning cell that still does not fit adds what it lacks to the last row it covers.
    /// </remarks>
    private List<Flow> Tabulate(
        IReadOnlyList<DeclaredRow> rows,
        IReadOnlyList<Band> grid,
        float[] columns,
        float x,
        float width,
        PendingMarker? pending)
    {
        var set = grid
            .Select((band, row) => band.Cells
                .Select(placement => (
                    placement.Cell,
                    Block: Cell(
                        placement,
                        columns,
                        x,
                        RightOf(band, placement),
                        BelowOf(grid, row, placement),
                        pending)))
                .ToList())
            .ToList();

        var lifted = set.Select(Aligned).ToList();

        float[] heights = [.. lifted.Select(row => row
            .Where(cell => cell.Cell.Down == 1)
            .Select(cell => cell.Block.Height + cell.Above)
            .DefaultIfEmpty(0)
            .Max())];

        for (int index = 0; index < lifted.Count; index++)
        {
            foreach ((DeclaredCell cell, BlockFlow block, float above) in lifted[index].Where(cell => cell.Cell.Down > 1))
            {
                int last = Math.Min(index + cell.Down, heights.Length) - 1;
                float have = Spanned(heights, index, cell.Down);

                heights[last] += Math.Max(0, block.Height + above - have);
            }
        }

        return [.. lifted.Select((row, index) => (Flow)Rank(rows[index], row, heights, index, grid[index].IsTied, x, width))];
    }

    /// <summary>Drops the cells of a row until the first lines of those aligned on the baseline sit on one.</summary>
    /// <remarks>
    /// This is what CSS gives a table cell by default, and what a row of mixed sizes needs: a label of 13 points
    /// beside a number of 28 reads as one line only when the two sit on the same baseline rather than starting
    /// at the same height. A cell holding no text at all has no baseline to share and stays where it was.
    /// </remarks>
    private static List<(DeclaredCell Cell, BlockFlow Block, float Above)> Aligned(
        List<(DeclaredCell Cell, BlockFlow Block)> row)
    {
        var owns = row
            .Select(cell => cell.Cell.Style.VerticalAlign == VerticalAlign.Baseline ? FirstBaseline(cell.Block) : null)
            .ToList();

        float deepest = owns.OfType<float>().DefaultIfEmpty(0).Max();

        return [.. row.Select((cell, index) => (cell.Cell, cell.Block, owns[index] is { } own ? deepest - own : 0f))];
    }

    /// <summary>How far below the top of a flow the first line of text in it sits, if it holds any.</summary>
    private static float? FirstBaseline(Flow flow)
    {
        if (flow is LineFlow line)
        {
            return line.Texts.Count == 0 ? null : line.Texts[0].Baseline;
        }

        if (flow is not BlockFlow block)
        {
            return null;
        }

        IReadOnlyList<float> offsets = Stacking.Offsets(block.Children);

        for (int index = 0; index < block.Children.Count; index++)
        {
            if (FirstBaseline(block.Children[index]) is { } baseline)
            {
                return block.Head + offsets[index] + baseline;
            }
        }

        return null;
    }

    private BlockFlow Cell(Placement placement, float[] columns, float x, BorderSide right, BorderSide below, PendingMarker? pending)
    {
        galley.Token.ThrowIfCancellationRequested();

        Style style = placement.Cell.Style with { Width = null, Margin = Sides.Zero };

        return galley.Block(
            placement.Cell.Element,
            style.IsCollapsed ? style with { Border = Shared(style.Border, right, below) } : style,
            x + Spanned(columns, 0, placement.At),
            Spanned(columns, placement.At, placement.Cell.Span),
            pending);
    }

    /// <summary>The border a cell keeps when the table draws one rule where two cells meet.</summary>
    /// <remarks>
    /// Where two cells touch, each of them wants a rule and the two are drawn side by side, so a table of
    /// one point lines comes out with two point lines between its cells and one at the edges. A cell gives up
    /// the side its neighbour also draws — the right and the bottom — and the neighbour draws the one rule.
    /// <para>
    /// What matters is that the neighbour really draws it. Giving the side up whenever a neighbour merely
    /// existed lost the rule altogether in the commonest table there is: cells that declare
    /// <c>border-bottom</c> and nothing else have no neighbour drawing a top, so every rule but the one under
    /// the last row simply vanished, and a specification of eight positions came out with no lines between
    /// them at all. A side is surrendered only to a rule at least as thick as itself, so the heavier of the
    /// two survives and a cell whose neighbour draws nothing keeps its own.
    /// </para>
    /// <para>
    /// Only the right and the bottom are weighed, which settles every table drawn in one weight. Where the
    /// two sides of one edge are declared at different thicknesses the heavier is kept but the lighter is not
    /// suppressed, so that edge comes out drawn twice; resolving it would mean naming the cell above a cell
    /// that several rows may span, and no document has yet asked for it.
    /// </para>
    /// </remarks>
    private static Border Shared(Border border, BorderSide right, BorderSide below) => border with
    {
        Right = right.Width >= border.Right.Width ? default : border.Right,
        Bottom = below.Width >= border.Bottom.Width ? default : border.Bottom,
    };

    /// <summary>The rule the cell to the right draws where the two meet, or none where no cell stands there.</summary>
    private static BorderSide RightOf(Band band, Placement placement) =>
        band.Cells
            .Where(other => other.At == placement.At + placement.Cell.Span)
            .Select(other => other.Cell.Style.Border.Left)
            .FirstOrDefault();

    /// <summary>The heaviest rule the cells beneath draw where they meet this one, or none where the table ends.</summary>
    /// <remarks>
    /// A cell spanning rows meets the row below the last one it covers, and a cell spanning columns may meet
    /// several cells there, each free to declare a rule of its own; the thickest of them is the one that would
    /// be drawn, so it is the one this cell has to weigh itself against.
    /// </remarks>
    private static BorderSide BelowOf(IReadOnlyList<Band> grid, int row, Placement placement)
    {
        int next = row + placement.Cell.Down;

        if (next >= grid.Count)
        {
            return default;
        }

        List<BorderSide> tops =
        [
            .. grid[next].Cells
                .Where(other => other.At < placement.At + placement.Cell.Span && other.At + other.Cell.Span > placement.At)
                .Select(other => other.Cell.Style.Border.Top),
        ];

        return tops.Count == 0 ? default : tops.MaxBy(side => side.Width);
    }

    private static RowFlow Rank(
        DeclaredRow row,
        IReadOnlyList<(DeclaredCell Cell, BlockFlow Block, float Above)> cells,
        float[] heights,
        int index,
        bool isTied,
        float x,
        float width)
    {
        var frame = new BlockFrame(
            row.Element.LocalName,
            x,
            width,
            row.Style.Border,
            default,
            row.Style.Background,
            row.Style.BreakBefore,
            row.Style.BreakAfter,
            row.Style.AvoidsBreakInside,
            row.Style.Orphans,
            row.Style.Widows,
            null,
            row.Style.BorderRadius);

        var stretched = cells
            .Select(cell => Stretched(cell.Block, Spanned(heights, index, cell.Cell.Down), cell.Cell.Style.VerticalAlign, cell.Above))
            .ToList();

        return new RowFlow(frame, stretched, row.IsHeader, heights[index], isTied, cells.Any(cell => cell.Cell.Down > 1));
    }

    /// <summary>The same cell made as tall as the row it sits in, holding its content where it was told to.</summary>
    /// <remarks>
    /// The room a short cell gains is added to its top padding rather than moved into the box, so the cell
    /// paints across the whole row while its content sits at the top, in the middle or at the foot of it.
    /// </remarks>
    private static BlockFlow Stretched(BlockFlow cell, float height, VerticalAlign align, float baseline)
    {
        float free = Math.Max(0, height - cell.Height - baseline);
        float above = baseline + align switch
        {
            VerticalAlign.Middle => free / 2,
            VerticalAlign.TextBottom => free,
            _ => 0,
        };

        Edges padding = cell.Frame.Padding with { Top = cell.Frame.Padding.Top + above };
        BlockFrame frame = cell.Frame with { Padding = padding, MinContentHeight = height - cell.Head - cell.Foot - above };

        return new BlockFlow(frame, cell.Children, 0, 0);
    }

    /// <summary>How wide each column is: what the markup declared, and otherwise what the content asks for.</summary>
    /// <remarks>
    /// A column with a declared width keeps it. The rest begin at the width of their longest word, grow towards
    /// the width they would take unbroken, and then share whatever is still free, so a table fills the space it
    /// was given. When even the longest words do not fit, every column keeps its own and the table overruns:
    /// setting a column narrower than a word it must hold would only hide the problem.
    /// </remarks>
    private float[] Columns(IReadOnlyList<Band> grid, float inner)
    {
        var placed = grid.SelectMany(band => band.Cells).ToList();
        int count = placed.Count == 0 ? 0 : placed.Max(placement => placement.At + placement.Cell.Span);
        float[] widths = new float[count];
        float[] most = new float[count];
        bool[] isAutomatic = new bool[count];

        for (int column = 0; column < count; column++)
        {
            var cells = placed
                .Where(placement => placement.Cell.Span == 1 && placement.At == column)
                .Select(placement => placement.Cell)
                .ToList();
            float? declared = cells.Select(cell => cell.Style.Width?.Resolve(inner)).FirstOrDefault(width => width is > 0);
            var wanted = cells.Select(cell => galley.Intrinsic(cell.Element, cell.Style, inner)).ToList();

            isAutomatic[column] = declared is null;
            widths[column] = declared ?? (wanted.Count == 0 ? 0 : wanted.Max(cell => cell.Min));
            most[column] = wanted.Count == 0 ? 0 : wanted.Max(cell => cell.Max);
        }

        foreach ((DeclaredCell cell, int at) in placed.Where(placement => placement.Cell.Span > 1))
        {
            (float min, float max) = galley.Intrinsic(cell.Element, cell.Style, inner);

            Widen(widths, at, cell.Span, min);
            Widen(most, at, cell.Span, max);
        }

        return Spread(widths, most, isAutomatic, inner);
    }

    private static float[] Spread(float[] widths, float[] most, bool[] isAutomatic, float inner)
    {
        float free = inner - widths.Sum();

        if (free <= 0)
        {
            return widths;
        }

        float room = 0;

        for (int column = 0; column < widths.Length; column++)
        {
            room += isAutomatic[column] ? Math.Max(0, most[column] - widths[column]) : 0;
        }

        if (room > 0)
        {
            float grown = Math.Min(free, room);

            for (int column = 0; column < widths.Length; column++)
            {
                widths[column] += isAutomatic[column] ? grown * Math.Max(0, most[column] - widths[column]) / room : 0;
            }

            free -= grown;
        }

        int automatic = isAutomatic.Count(column => column);

        if (free > 0 && automatic > 0)
        {
            for (int column = 0; column < widths.Length; column++)
            {
                widths[column] += isAutomatic[column] ? free / automatic : 0;
            }
        }

        return widths;
    }

    /// <summary>The rows of a table in the order they are set, with the foot of the table last.</summary>
    /// <remarks>
    /// A <c>tfoot</c> is the foot of a table wherever it is written. HTML 4 required it before the body and
    /// HTML 5 allows it at either end, so the markup's order says nothing about where the rows belong: read
    /// as written, a foot declared first came out between the heading and the first row of the body, which is
    /// how a line promising a continuation ended up above the very rows it spoke of. Only the foot is moved —
    /// every other row keeps the place the markup gave it, including a row of its own <c>th</c> cells, which
    /// heads the rows it stands above rather than the table.
    /// </remarks>
    private List<DeclaredRow> Rows(IElement element, Style style)
    {
        List<DeclaredRow> rows = [];
        List<DeclaredRow> foot = [];

        Collect(element, style, rows, foot, isHeader: false, isFooter: false, isContinued: false);

        return [.. rows, .. foot];
    }

    private void Collect(
        IElement parent,
        Style style,
        List<DeclaredRow> rows,
        List<DeclaredRow> foot,
        bool isHeader,
        bool isFooter,
        bool isContinued)
    {
        foreach (IElement child in parent.Children)
        {
            Style computed = galley.Compute(child, style);

            if (computed.Display == Display.None)
            {
                continue;
            }

            if (Galley.IsTag(child, "tr"))
            {
                List<DeclaredCell> cells = Cells(child, computed);

                (isFooter ? foot : rows).Add(
                    new DeclaredRow(child, computed, isHeader || IsHeading(cells), isContinued, cells));
                continue;
            }

            if (Galley.IsTag(child, "thead") || Galley.IsTag(child, "tbody") || Galley.IsTag(child, "tfoot"))
            {
                Collect(
                    child,
                    computed,
                    rows,
                    foot,
                    isHeader || Galley.IsTag(child, "thead"),
                    isFooter || Galley.IsTag(child, "tfoot"),
                    isContinued || (Galley.IsTag(child, "tfoot") && child.HasAttribute("continued")));
            }
        }
    }

    private List<DeclaredCell> Cells(IElement row, Style style)
    {
        List<DeclaredCell> cells = [];

        foreach (IElement child in row.Children)
        {
            if (!Galley.IsTag(child, "td") && !Galley.IsTag(child, "th"))
            {
                continue;
            }

            Style computed = galley.Compute(child, style);

            if (computed.Display != Display.None)
            {
                cells.Add(new DeclaredCell(child, computed, SpanOf(child, "colspan"), SpanOf(child, "rowspan")));
            }
        }

        return cells;
    }

    /// <summary>How many columns or rows a cell covers, never more than a table may hold.</summary>
    /// <remarks>
    /// The grid is laid out in arrays as wide as the furthest column a cell reaches, so a span the markup is
    /// free to choose is an allocation the markup is free to choose: measured on this engine,
    /// <c>colspan="2000000"</c> cost 826 MB from a single cell and <c>colspan="2147483647"</c> threw
    /// <see cref="OutOfMemoryException"/> before a page could be set. Markup is often written by users, so the
    /// span is capped where HTML caps it and the document says what it did instead of taking the process down.
    /// </remarks>
    private int SpanOf(IElement element, string attribute)
    {
        if (!int.TryParse(element.GetAttribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out int span) || span <= 1)
        {
            return 1;
        }

        if (span <= MaxSpan)
        {
            return span;
        }

        galley.Remark(new Note(
            NoteKind.SpanCapped,
            attribute,
            $"A cell asks for {attribute}=\"{span}\", more than a table may hold; it covers {MaxSpan} instead."));

        return MaxSpan;
    }

    /// <summary>Where every cell of the table sits, walking each row from the left past the columns already taken.</summary>
    /// <remarks>
    /// A cell with a <c>rowspan</c> holds its columns for the rows beneath it, and the cells of those rows step
    /// over what it holds. Without that the second row of a table with a spanning cell would start at column
    /// zero and slide underneath it.
    /// </remarks>
    private static List<Band> Grid(IReadOnlyList<DeclaredRow> rows)
    {
        List<Band> grid = [];
        Dictionary<int, int> held = [];

        foreach (DeclaredRow row in rows)
        {
            List<Placement> placements = [];
            bool isTied = held.Values.Any(rest => rest > 0);
            int at = 0;

            foreach (DeclaredCell cell in row.Cells)
            {
                while (held.GetValueOrDefault(at) > 0)
                {
                    at++;
                }

                placements.Add(new Placement(cell, at));
                at += cell.Span;
            }

            grid.Add(new Band(placements, isTied));

            foreach (int column in held.Keys.ToList())
            {
                held[column]--;
            }

            foreach (Placement placement in placements.Where(placement => placement.Cell.Down > 1))
            {
                for (int column = placement.At; column < placement.At + placement.Cell.Span; column++)
                {
                    held[column] = placement.Cell.Down - 1;
                }
            }
        }

        return grid;
    }

    /// <summary>The width of a run of columns, which is what a cell spanning them is given.</summary>
    private static float Spanned(float[] columns, int at, int span)
    {
        float width = 0;

        for (int column = at; column < at + span && column < columns.Length; column++)
        {
            width += columns[column];
        }

        return width;
    }

    /// <summary>Widens a run of columns to hold a cell that spans them, sharing what they lack equally.</summary>
    /// <remarks>
    /// A spanning cell is measured after the columns have taken what the cells of their own asked for, and it
    /// only adds what those cells did not already provide. Giving it a say before them would let one wide
    /// heading decide the shape of a table whose rows never needed the room.
    /// </remarks>
    private static void Widen(float[] widths, int at, int span, float wanted)
    {
        float have = Spanned(widths, at, span);

        if (wanted <= have)
        {
            return;
        }

        float each = (wanted - have) / span;

        for (int column = at; column < at + span && column < widths.Length; column++)
        {
            widths[column] += each;
        }
    }

    /// <summary>Whether a row is a heading of its own, written as cells that are all <c>th</c>.</summary>
    private static bool IsHeading(IReadOnlyList<DeclaredCell> cells) =>
        cells.Count > 0 && cells.All(cell => Galley.IsTag(cell.Element, "th"));

    /// <summary>One cell as the markup declared it, before it is given the columns it sits in.</summary>
    /// <remarks>
    /// The span is how many columns the cell covers and the depth how many rows, one each unless
    /// <c>colspan</c> and <c>rowspan</c> say otherwise.
    /// </remarks>
    private sealed record DeclaredCell(IElement Element, Style Style, int Span, int Down);

    /// <summary>A cell and the column it starts at, once the cells reaching down from earlier rows are counted.</summary>
    private sealed record Placement(DeclaredCell Cell, int At);

    /// <summary>One row of the grid: where its cells sit, and whether a cell from above reaches into it.</summary>
    private sealed record Band(IReadOnlyList<Placement> Cells, bool IsTied);

    /// <summary>One row as the markup declared it, with its cells and whether it heads or continues the table.</summary>
    private sealed record DeclaredRow(
        IElement Element,
        Style Style,
        bool IsHeader,
        bool IsContinued,
        IReadOnlyList<DeclaredCell> Cells);
}
