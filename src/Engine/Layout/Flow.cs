using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>Anything that stacks vertically in the galley: a block box or one line of text.</summary>
internal abstract class Flow
{
    public abstract float Height { get; }

    public virtual float MarginTop => 0;

    public virtual float MarginBottom => 0;

    /// <summary>Whether a page has to end before this, however much room is left.</summary>
    public virtual bool ForcesBreakBefore => false;

    /// <summary>Whether a page has to end after this, however much room is left.</summary>
    public virtual bool ForcesBreakAfter => false;

    /// <summary>Whether a page has to end somewhere inside this, however much room is left.</summary>
    /// <remarks>
    /// Pagination only looks inside what does not fit, so a forced break deeper down has to be announced all
    /// the way up or it is never reached. A row announces it as a block does: a specification nested in a
    /// margin layout sits a cell and a row below the page, and asking only blocks left its last break unread
    /// — eleven positions came out on a page that was told to hold eight.
    /// </remarks>
    public virtual bool HasForcedBreakInside => false;
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

    /// <summary>Why the line ends where it does, which decides whether the next one carries the same paragraph on.</summary>
    public LineEnd End { get; init; } = LineEnd.Last;

    /// <summary>How the block the line belongs to aligns its lines.</summary>
    public TextAlign Align { get; init; }
}

/// <summary>Why a line ends: it ran out of room at a space or inside a word, or the text told it to.</summary>
/// <remarks>
/// A renderer that lets the text flow again — a deck, whose text boxes wrap on their own — has to put the
/// lines of a paragraph back together, and needs to know what stood between them: a space that was dropped
/// at the break, nothing at all where a word was parted, or the end of the paragraph.
/// </remarks>
internal enum LineEnd
{
    /// <summary>The line ran out of room at a space, which the break swallowed.</summary>
    Space,

    /// <summary>The line ran out of room where a word may be parted without a space: after a hyphen or at a soft hyphen.</summary>
    Word,

    /// <summary>A word wider than the whole line was cut between two of its letters.</summary>
    Cut,

    /// <summary>A forced break, such as <c>br</c>, ended the line.</summary>
    Forced,

    /// <summary>The content ran out: the last line of its block.</summary>
    Last,
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
    TextDecoration Decoration)
{
    /// <summary>Which number of a running line the text is, if it is one.</summary>
    public TextField Field { get; init; }

    /// <summary>Whether the text is a list item's marker, hung before the line rather than part of it.</summary>
    public bool IsMarker { get; init; }
}

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

    /// <summary>Whether the markup asks for a page to end before this row.</summary>
    /// <remarks>
    /// A row answers this as a block does, because a table is paginated by the same machinery and asked the
    /// same questions. Until it did, <c>break-before</c> and <c>break-after</c> written on a <c>tr</c> were
    /// read by the cascade, carried on the frame and then never looked at — a specification that wanted five
    /// positions on its opening page and eight thereafter had no way to say so.
    /// </remarks>
    public override bool ForcesBreakBefore => !isSplitAbove && frame.BreakBefore == BreakRule.Page;

    /// <inheritdoc cref="ForcesBreakBefore"/>
    public override bool ForcesBreakAfter => !isSplitBelow && frame.BreakAfter == BreakRule.Page;

    /// <inheritdoc/>
    public override bool HasForcedBreakInside { get; } = cells.Any(cell => cell.HasForcedBreakInside);

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
    /// <summary>Whether every fragment keeps this box's own padding and border instead of one set between them.</summary>
    /// <remarks>
    /// CSS <c>box-decoration-break</c>: <c>slice</c> by default, where a box cut by a page boundary is drawn
    /// as one box opened on the first page and closed on the last, so the cut sides carry neither border nor
    /// padding. <c>clone</c> makes each fragment whole — which is what a section carried over needs when the
    /// air above its content is to be found on every page it reaches, not only the one it started on.
    /// </remarks>
    public bool ClonesDecoration { get; init; }

    /// <summary>Rows set at the foot of every fragment but the last, where the block goes on overleaf.</summary>
    /// <remarks>
    /// These are not children. A foot that says the document continues has nothing to say on the page where
    /// it stops, so it cannot be part of what the block holds: held as a child it would be set once, at the
    /// end, which is the one place its sentence is false. It rides on the frame instead, which every fragment
    /// of a block carries unchanged, and pagination sets it on each fragment that turns out to have a
    /// continuation.
    /// </remarks>
    public IReadOnlyList<Flow> Continued { get; init; } = [];

    /// <summary>What the block sets again at the top of every fragment after the first, where it carries on.</summary>
    /// <remarks>
    /// The mirror of <see cref="Continued"/>, and held the same way and for the same reason: a heading that
    /// reads "continued" has nothing to say on the page where the block starts. A cell keeps this even when
    /// its own content ran out on the first page, which is what a note in a margin column is — short beside
    /// something long, and needing to say something else where the long thing goes on.
    /// </remarks>
    public IReadOnlyList<Flow> Resumed { get; init; } = [];

    /// <summary>How far a table cell's content was lowered inside its top padding to sit as the cell is aligned.</summary>
    /// <remarks>
    /// A cell made as tall as its row takes the room it gains into its top padding, so the padding alone no longer
    /// says what the markup declared. A renderer with cells of its own — a deck — aligns them itself and needs the
    /// declared padding back.
    /// </remarks>
    public float Lift { get; init; }

    /// <summary>The <c>vertical-align</c> of a table cell, which a deck turns into the anchor of its own cell.</summary>
    public VerticalAlign CellAlign { get; init; }

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
        Head = isSplitAbove && !frame.ClonesDecoration ? 0 : frame.Border.Top.Width + frame.Padding.Top;
        Foot = isSplitBelow && !frame.ClonesDecoration ? 0 : frame.Border.Bottom.Width + frame.Padding.Bottom;

        float content = Stacking.Height(children);

        Height = Head + (frame.MinContentHeight is { } minimum ? Math.Max(content, minimum) : content) + Foot;
    }

    private bool? _hasForcedBreakInside;

    public BlockFrame Frame { get; }

    public IReadOnlyList<Flow> Children { get; }

    public override float MarginTop { get; }

    public override float MarginBottom { get; }

    public bool IsSplitAbove { get; }

    public bool IsSplitBelow { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Asked for lazily: a cut makes a new block of everything still waiting on every page, and answering this in
    /// the constructor walked all of it each time — one of the passes that made a document of 40 000 paragraphs
    /// cost four times what one of 20 000 did.
    /// </remarks>
    public override bool HasForcedBreakInside => _hasForcedBreakInside ??= HasForcedBreakAmong(Children);

    public float Head { get; }

    public float Foot { get; }

    public override float Height { get; }

    public override bool ForcesBreakBefore =>
        !IsSplitAbove && (Frame.BreakBefore == BreakRule.Page || Children is [{ ForcesBreakBefore: true }, ..]);

    public override bool ForcesBreakAfter =>
        !IsSplitBelow && (Frame.BreakAfter == BreakRule.Page || Children is [.., { ForcesBreakAfter: true }]);

    public BlockFlow With(IReadOnlyList<Flow> children, float marginTop, float marginBottom, bool isSplitAbove, bool isSplitBelow) =>
        new(Frame, children, marginTop, marginBottom, isSplitAbove, isSplitBelow);

    /// <summary>The same block with a foot to set on every fragment of it but the last.</summary>
    public BlockFlow Continuing(IReadOnlyList<Flow> foot) =>
        new(Frame with { Continued = foot }, Children, MarginTop, MarginBottom, IsSplitAbove, IsSplitBelow);

    /// <summary>The same block with a heading to set on every fragment of it but the first.</summary>
    public BlockFlow Resuming(IReadOnlyList<Flow> heading) =>
        new(Frame with { Resumed = heading }, Children, MarginTop, MarginBottom, IsSplitAbove, IsSplitBelow);

    /// <summary>The same block opened by the heading it sets wherever it resumes.</summary>
    /// <remarks>Given to a fragment that follows another, so what it holds reads as a continuation.</remarks>
    public BlockFlow Headed() =>
        Frame.Resumed.Count == 0 ? this : new(Frame, [.. Frame.Resumed, .. Children], MarginTop, MarginBottom, IsSplitAbove, IsSplitBelow);

    /// <summary>The same block with its continued foot set at the end of what it holds.</summary>
    /// <remarks>Given to a fragment that has a continuation, once pagination has left the room for it.</remarks>
    public BlockFlow Footed() =>
        new(
            Frame with { Continued = [] },
            [.. Children, .. Frame.Continued],
            MarginTop,
            MarginBottom,
            IsSplitAbove,
            IsSplitBelow);

    /// <summary>The same box without the height its row lent it, so a cut measures only what it holds.</summary>
    public BlockFlow Bare() =>
        new(Frame with { MinContentHeight = null }, Children, MarginTop, MarginBottom, IsSplitAbove, IsSplitBelow);

    /// <summary>The same box made as tall as the row, or the fragment of a row, it now belongs to.</summary>
    public BlockFlow StretchedTo(float height) =>
        new(Frame with { MinContentHeight = height - Head - Foot }, Children, MarginTop, MarginBottom, IsSplitAbove, IsSplitBelow);

    private static bool HasForcedBreakAmong(IReadOnlyList<Flow> children)
    {
        int opening = 0;

        while (opening < children.Count && children[opening] is RowFlow { IsHeader: true })
        {
            opening++;
        }

        for (int index = 0; index < children.Count; index++)
        {
            Flow child = children[index];
            bool isForcedBefore = index > opening && child.ForcesBreakBefore;
            bool isForcedAfter = index < children.Count - 1 && child.ForcesBreakAfter;

            if (isForcedBefore || isForcedAfter || child.HasForcedBreakInside)
            {
                return true;
            }
        }

        return false;
    }
}
