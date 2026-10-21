using Snail.Toolkit.HtmlComposer.Engine.Layout;
using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Pagination;

/// <summary>One fragment of a table read back as a grid: its columns, its rows and the rule on every edge.</summary>
/// <remarks>
/// The galley sets a table as rows of cells placed side by side and forgets the grid it measured them on. The
/// grid is found again from where the cells stand: every left and right edge of a cell is a column boundary, and
/// a cell covers the columns between its own two edges. That holds for any table whose rows are simple, which is
/// the only kind given back — a cell spanning rows or a table inside a cell has no place in such a grid.
/// </remarks>
internal sealed class Grid(BlockFlow table)
{
    /// <summary>How close two edges may lie and still be one column boundary, which rounding alone moves apart.</summary>
    private const float Join = 0.05f;

    private readonly List<RenderBox> _boxes = [];

    private readonly List<RenderBox> _inside = [];

    private readonly List<ImageBox> _pictures = [];

    private readonly List<(RowFlow Row, float Top, List<Placed> Cells)> _rows = [];

    /// <summary>Whether a block is a table that can be given back as a grid of simple rows.</summary>
    public static bool IsGrid(BlockFlow block) =>
        block.Children.Count > 0
        && block.Children.All(child => child is RowFlow { IsTied: false, HoldsSpan: false, Cells.Count: > 0 } row && row.Cells.All(IsSimple));

    public void Own(RenderBox box) => _boxes.Add(box);

    public void Open(RowFlow row, float top) => _rows.Add((row, top, []));

    public void Add(BlockFlow cell, RenderBox? box, IReadOnlyList<TextParagraph> paragraphs, IReadOnlyList<RenderBox> inside, IReadOnlyList<ImageBox> pictures)
    {
        if (box is not null)
        {
            _boxes.Add(box);
        }

        ImageBox? picture = paragraphs.Count == 0 && pictures.Count == 1 ? pictures[0] : null;

        _inside.AddRange(inside);
        _pictures.AddRange(picture is null ? pictures : []);

        _rows[^1].Cells.Add(new Placed(cell, paragraphs, picture));
    }

    public TableFrame Close()
    {
        float[] edges = Edges();
        float[] columns = new float[Math.Max(0, edges.Length - 1)];

        for (int column = 0; column < columns.Length; column++)
        {
            columns[column] = edges[column + 1] - edges[column];
        }

        List<TableRow> rows = [];

        for (int index = 0; index < _rows.Count; index++)
        {
            (RowFlow row, float top, List<Placed> cells) = _rows[index];

            rows.Add(new TableRow(top, row.Height, row.IsHeader, [.. cells.Select(cell => CellOf(cell, index, edges))]));
        }

        float first = _rows.Count == 0 ? 0 : _rows[0].Top;
        float height = _rows.Sum(row => row.Row.Height);
        float left = edges.Length == 0 ? table.Frame.X : edges[0];
        float width = edges.Length == 0 ? 0 : edges[^1] - edges[0];

        var frame = new TableFrame(new Rect(left, first, width, height), columns, rows, _boxes)
        {
            CellBoxes = _inside,
            CellImages = _pictures,
        };

        return frame;
    }

    private static bool IsSimple(Flow flow) => flow switch
    {
        RowFlow => false,
        BlockFlow block => block.Children.All(IsSimple),
        _ => true,
    };

    /// <summary>Every column boundary of the fragment, left to right.</summary>
    private float[] Edges()
    {
        List<float> edges = [];

        foreach ((_, _, List<Placed> cells) in _rows)
        {
            foreach (Placed cell in cells)
            {
                Mark(edges, cell.Flow.Frame.X);
                Mark(edges, cell.Flow.Frame.X + cell.Flow.Frame.Width);
            }
        }

        edges.Sort();

        return [.. edges];
    }

    private static void Mark(List<float> edges, float edge)
    {
        if (!edges.Any(known => Math.Abs(known - edge) < Join))
        {
            edges.Add(edge);
        }
    }

    private static int IndexOf(float[] edges, float edge)
    {
        int nearest = 0;

        for (int index = 1; index < edges.Length; index++)
        {
            nearest = Math.Abs(edges[index] - edge) < Math.Abs(edges[nearest] - edge) ? index : nearest;
        }

        return nearest;
    }

    private TableCell CellOf(Placed placed, int row, float[] edges)
    {
        BlockFlow cell = placed.Flow;
        BlockFrame frame = cell.Frame;
        (RowFlow flow, float top, _) = _rows[row];
        int column = IndexOf(edges, frame.X);
        int span = Math.Max(1, IndexOf(edges, frame.X + frame.Width) - column);
        CellAlignment alignment = frame.CellAlign switch
        {
            VerticalAlign.Middle => CellAlignment.Middle,
            VerticalAlign.TextBottom => CellAlignment.Bottom,
            _ => CellAlignment.Top,
        };

        float head = Math.Max(0, cell.Head - frame.Lift);
        float above = alignment == CellAlignment.Top && placed.Paragraphs.Count > 0
            ? placed.Paragraphs[0].Lines[0].Top - top
            : head;
        var padding = new Edges(
            above,
            frame.Border.Right.Width + frame.Padding.Right,
            cell.Foot,
            frame.Border.Left.Width + frame.Padding.Left);

        return new TableCell(
            column,
            span,
            new Rect(frame.X, top, frame.Width, flow.Height),
            padding,
            alignment,
            RuleAbove(row, placed),
            RuleBelow(row, placed),
            frame.Background ?? flow.Frame.Background,
            placed.Paragraphs,
            placed.Picture);
    }

    /// <summary>The rule along the top of a cell: the heaviest of what it, its row and whatever stands above declared there.</summary>
    private BorderSide RuleAbove(int row, Placed placed)
    {
        (RowFlow flow, _, _) = _rows[row];
        BlockFrame cell = placed.Flow.Frame;
        BorderSide rule = Heavier(default, flow.IsSplitAbove ? default : flow.Frame.Border.Top);

        rule = Heavier(rule, placed.Flow.IsSplitAbove ? default : cell.Border.Top);

        if (row == 0)
        {
            return Heavier(rule, table.IsSplitAbove ? default : table.Frame.Border.Top);
        }

        (RowFlow above, _, List<Placed> cells) = _rows[row - 1];

        rule = Heavier(rule, above.IsSplitBelow ? default : above.Frame.Border.Bottom);

        foreach (Placed other in cells.Where(other => Overlaps(other.Flow.Frame, cell)))
        {
            rule = Heavier(rule, other.Flow.IsSplitBelow ? default : other.Flow.Frame.Border.Bottom);
        }

        return rule;
    }

    /// <summary>The rule along the foot of a cell, found the same way as the one along its top.</summary>
    private BorderSide RuleBelow(int row, Placed placed)
    {
        (RowFlow flow, _, _) = _rows[row];
        BlockFrame cell = placed.Flow.Frame;
        BorderSide rule = Heavier(default, flow.IsSplitBelow ? default : flow.Frame.Border.Bottom);

        rule = Heavier(rule, placed.Flow.IsSplitBelow ? default : cell.Border.Bottom);

        if (row == _rows.Count - 1)
        {
            return Heavier(rule, table.IsSplitBelow ? default : table.Frame.Border.Bottom);
        }

        (RowFlow below, _, List<Placed> cells) = _rows[row + 1];

        rule = Heavier(rule, below.IsSplitAbove ? default : below.Frame.Border.Top);

        foreach (Placed other in cells.Where(other => Overlaps(other.Flow.Frame, cell)))
        {
            rule = Heavier(rule, other.Flow.IsSplitAbove ? default : other.Flow.Frame.Border.Top);
        }

        return rule;
    }

    private static bool Overlaps(BlockFrame one, BlockFrame other) =>
        one.X < other.X + other.Width - Join && one.X + one.Width > other.X + Join;

    private static BorderSide Heavier(BorderSide current, BorderSide candidate) =>
        candidate.IsVisible && candidate.Width > current.Width ? candidate : current;

    /// <summary>A cell as it was placed, with the paragraphs and the picture it was found to hold.</summary>
    private sealed record Placed(BlockFlow Flow, IReadOnlyList<TextParagraph> Paragraphs, ImageBox? Picture);
}
