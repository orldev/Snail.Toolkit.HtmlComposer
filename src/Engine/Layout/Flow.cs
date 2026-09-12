using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>Anything that stacks vertically in the galley: a block box or one line of text.</summary>
internal abstract class Flow
{
    public abstract float Height { get; }

    public virtual float MarginTop => 0;

    public virtual float MarginBottom => 0;
}

/// <summary>One line box, placed horizontally and waiting for a page to give it a vertical position.</summary>
internal sealed class LineFlow(
    float height,
    IReadOnlyList<LineText> texts,
    IReadOnlyList<LinePicture> pictures,
    IReadOnlyList<LineMark> marks,
    IReadOnlyList<LineBox> boxes) : Flow
{
    public override float Height => height;

    public IReadOnlyList<LineText> Texts => texts;

    public IReadOnlyList<LinePicture> Pictures => pictures;

    public IReadOnlyList<LineMark> Marks => marks;

    /// <summary>What is painted behind the inline content: the backgrounds and borders of its runs.</summary>
    public IReadOnlyList<LineBox> Boxes => boxes;
}

/// <summary>The background and border of one run of inline content, measured from the top of its line.</summary>
/// <remarks>
/// An inline box is as tall as the run's own line height, not the line's: a small span inside a large one is
/// painted around its own text. It is carried apart from the text so that the renderer keeps painting boxes
/// before glyphs without sorting anything.
/// </remarks>
internal sealed record LineBox(float X, float Top, float Width, float Height, Rgba? Fill, Border Border, float Radius);

/// <summary>A list bullet drawn as a shape: its left edge is final, its top is measured from the top of the line.</summary>
/// <remarks>
/// Bullets are shapes rather than glyphs: PT Sans, like many text faces, has no glyph for a white bullet or a
/// small square, and a missing glyph prints as a crossed box. The shape is a third of an em across, centred
/// 0.3 em above the baseline, which is about where a bullet glyph sits in common text faces.
/// </remarks>
internal sealed record LineMark(float X, float Top, float Size, Rgba? Fill, BorderSide Outline, float Radius)
{
    public static LineMark Bullet(ListMarker kind, float right, float baseline, float fontSize, Rgba color)
    {
        float size = fontSize / 3;
        float x = right - size;
        float top = baseline - (fontSize * 0.3f) - (size / 2);

        return kind switch
        {
            ListMarker.Circle => new LineMark(x, top, size, null, new BorderSide(Math.Max(0.5f, fontSize / 16), color), size / 2),
            ListMarker.Square => new LineMark(x, top, size, color, default, 0),
            _ => new LineMark(x, top, size, color, default, size / 2),
        };
    }
}

/// <summary>Text on a line: its left edge is final, its baseline is measured from the top of the line.</summary>
internal sealed record LineText(
    string Text,
    float X,
    float Baseline,
    float Width,
    Font Font,
    Rgba Color,
    TextDecoration Decoration);

/// <summary>A picture on a line: its left edge is final, its top is measured from the top of the line.</summary>
internal sealed record LinePicture(float X, float Top, float Width, float Height, Image Image, Rect? Source = null);

/// <summary>One row of a table: its cells set side by side, every one as tall as the tallest.</summary>
/// <remarks>
/// A table is an ordinary block whose children are rows, so a table too tall for the page is cut between its
/// rows by the same pagination as any other block, and the header rows are repeated at the top of what follows.
/// A row too tall for what is left is cut across as well, every cell at the same height, and the fragments
/// carry the flags that keep a rule from being drawn where the cut was made. A row that holds or is held by a
/// cell spanning rows is never cut: that cell belongs to rows the row itself cannot see.
/// </remarks>
internal sealed class RowFlow(
    BlockFrame frame,
    IReadOnlyList<BlockFlow> cells,
    bool isHeader,
    float height,
    bool isTied,
    bool holdsSpan = false,
    bool isSplitAbove = false,
    bool isSplitBelow = false) : Flow
{
    public BlockFrame Frame => frame;

    public IReadOnlyList<BlockFlow> Cells => cells;

    /// <summary>Whether this row belongs to the head of the table and is repeated on every fragment.</summary>
    public bool IsHeader => isHeader;

    /// <summary>Whether a cell from the row above reaches into this one, so the two may not be parted.</summary>
    public bool IsTied => isTied;

    /// <summary>Whether a cell of this row reaches into the rows below, so the row may not be cut across.</summary>
    public bool HoldsSpan => holdsSpan;

    public bool IsSplitAbove => isSplitAbove;

    public bool IsSplitBelow => isSplitBelow;

    /// <summary>The height of the row itself, which a cell spanning several rows may exceed.</summary>
    public override float Height { get; } = height;

    public RowFlow With(IReadOnlyList<BlockFlow> fragments, float height, bool isSplitAbove, bool isSplitBelow) =>
        new(frame, fragments, isHeader, height, isTied, holdsSpan, isSplitAbove, isSplitBelow);
}

/// <summary>What a block looks like and how it may break, shared by every fragment the pages cut it into.</summary>
internal sealed record BlockFrame(
    string Element,
    float X,
    float Width,
    Border Border,
    Edges Padding,
    Rgba? Background,
    BreakRule BreakBefore,
    BreakRule BreakAfter,
    bool AvoidsBreakInside,
    int Orphans,
    int Widows,
    float? MinContentHeight,
    float Radius = 0)
{
    public bool IsAnonymous => Element.Length == 0;
}

/// <summary>A block box, or the fragment of one that lands on a single page.</summary>
/// <remarks>
/// A class rather than a record so that its height is computed once, when the fragment is made: pagination
/// asks for heights at every level of every attempt, and a record copied with <c>with</c> would carry a
/// height computed for other children.
/// <para>
/// The least content height a row lends its cells holds for a fragment as well. A row cut across two pages
/// gives every fragment the height of the half it belongs to, and a fragment that ignored the minimum would
/// leave the shorter cells of a half row painted short while the rest of the grid stood full height.
/// </para>
/// </remarks>
internal sealed class BlockFlow : Flow
{
    public BlockFlow(
        BlockFrame frame,
        IReadOnlyList<Flow> children,
        float marginTop,
        float marginBottom,
        bool isSplitAbove = false,
        bool isSplitBelow = false)
    {
        Frame = frame;
        Children = children;
        MarginTop = marginTop;
        MarginBottom = marginBottom;
        IsSplitAbove = isSplitAbove;
        IsSplitBelow = isSplitBelow;
        HasForcedBreakInside = HasForcedBreakAmong(children);
        Head = isSplitAbove ? 0 : frame.Border.Top.Width + frame.Padding.Top;
        Foot = isSplitBelow ? 0 : frame.Border.Bottom.Width + frame.Padding.Bottom;

        float content = Stacking.Height(children);

        Height = Head + (frame.MinContentHeight is { } minimum ? Math.Max(content, minimum) : content) + Foot;
    }

    public BlockFrame Frame { get; }

    public IReadOnlyList<Flow> Children { get; }

    public override float MarginTop { get; }

    public override float MarginBottom { get; }

    public bool IsSplitAbove { get; }

    public bool IsSplitBelow { get; }

    /// <summary>Whether a page has to end somewhere inside this block, however much room there is.</summary>
    /// <remarks>
    /// Pagination only looks inside a block that does not fit. Without this, a forced break nested in a block
    /// that fits on the page, which is every <c>break-before: page</c> in a short document, would never be seen.
    /// </remarks>
    public bool HasForcedBreakInside { get; }

    public float Head { get; }

    public float Foot { get; }

    public override float Height { get; }

    public bool ForcesBreakBefore =>
        !IsSplitAbove && (Frame.BreakBefore == BreakRule.Page || Children is [BlockFlow { ForcesBreakBefore: true }, ..]);

    public bool ForcesBreakAfter =>
        !IsSplitBelow && (Frame.BreakAfter == BreakRule.Page || Children is [.., BlockFlow { ForcesBreakAfter: true }]);

    public BlockFlow With(IReadOnlyList<Flow> children, float marginTop, float marginBottom, bool isSplitAbove, bool isSplitBelow) =>
        new(Frame, children, marginTop, marginBottom, isSplitAbove, isSplitBelow);

    /// <summary>The same box without the height its row lent it, so a cut measures only what it holds.</summary>
    public BlockFlow Bare() =>
        new(Frame with { MinContentHeight = null }, Children, MarginTop, MarginBottom, IsSplitAbove, IsSplitBelow);

    /// <summary>The same box made as tall as the row, or the fragment of a row, it now belongs to.</summary>
    public BlockFlow StretchedTo(float height) =>
        new(Frame with { MinContentHeight = height - Head - Foot }, Children, MarginTop, MarginBottom, IsSplitAbove, IsSplitBelow);

    private static bool HasForcedBreakAmong(IReadOnlyList<Flow> children)
    {
        for (int index = 0; index < children.Count; index++)
        {
            if (children[index] is not BlockFlow child)
            {
                continue;
            }

            bool isForcedBefore = index > 0 && child.ForcesBreakBefore;
            bool isForcedAfter = index < children.Count - 1 && child.ForcesBreakAfter;

            if (isForcedBefore || isForcedAfter || child.HasForcedBreakInside)
            {
                return true;
            }
        }

        return false;
    }
}
