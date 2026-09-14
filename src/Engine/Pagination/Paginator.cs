using Snail.Toolkit.HtmlComposer.Engine.Layout;
using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Pagination;

/// <summary>Cuts the galley into pages, breaking between lines and between blocks but never through either.</summary>
/// <remarks>
/// Each page takes as much of what remains as fits. A block cut by the page boundary becomes two fragments:
/// the part that fits stays, and the rest opens the next page without its top border, padding or margin.
/// Where to cut follows CSS fragmentation: forced breaks first, then avoid rules, then orphans and widows.
/// When nothing can be honoured the first line or box is placed anyway, so every page makes progress and a
/// single oversized picture cannot loop forever.
/// </remarks>
internal static class Paginator
{
    private const float Tolerance = 0.01f;

    public static IReadOnlyList<Page> Paginate(BlockFlow galley, PageSetup setup, int first, CancellationToken ct)
    {
        Rect content = setup.Content;
        List<Page> pages = [];
        BlockFlow? remaining = galley;

        while (remaining is not null)
        {
            ct.ThrowIfCancellationRequested();

            float top = remaining.MarginTop;
            Cut cut = Split(remaining, content.Height - top, mustProgress: true);

            if (cut.Head is not { } head || ReferenceEquals(cut.Tail, remaining))
            {
                throw new InvalidOperationException(
                    "A page that has to make progress took nothing from the document, which no markup can ask for.");
            }

            pages.Add(Paint(head, content.Y + top, setup, first + pages.Count));
            remaining = cut.Tail as BlockFlow;
        }

        return pages;
    }

    private static Cut Split(Flow flow, float available, bool mustProgress) => flow switch
    {
        BlockFlow block => SplitBlock(block, available, mustProgress),
        RowFlow row => SplitRow(row, available, mustProgress),
        _ when flow.Height <= available + Tolerance || mustProgress => new Cut(flow, null),
        _ => new Cut(null, flow),
    };

    /// <summary>Cuts one row of a table across, carrying every column onto the next page together.</summary>
    /// <remarks>
    /// Each cell is cut on its own and the fragments are then made as tall as the tallest of them, so the
    /// backgrounds and rules of a half row stand level as they did when the row was whole. A cell is stripped
    /// of the height its row lent it before it is cut, or it would report the height of the whole row and
    /// nothing would ever fit; the fragments are stretched again afterwards. A column whose content ran out
    /// keeps an empty box on the continuation: the row goes on, and a column stopping early would leave a
    /// hole in the grid.
    /// <para>
    /// A row holding or held by a cell that spans rows is never cut, because that cell belongs to rows this
    /// one cannot see; it moves whole, as every row did before. A row is cut only where its cells can spare
    /// the lines: orphans and widows are counted inside each cell by the same rules as anywhere else, so a
    /// row that can give up nothing moves down instead.
    /// </para>
    /// </remarks>
    private static Cut SplitRow(RowFlow row, float available, bool mustProgress)
    {
        if (row.Height <= available + Tolerance && !row.HasForcedBreakInside)
        {
            return new Cut(row, null);
        }

        if (row.IsTied || row.HoldsSpan || row.Frame.AvoidsBreakInside || row.Cells.Count == 0)
        {
            return mustProgress ? new Cut(row, null) : new Cut(null, row);
        }

        var cuts = row.Cells.Select(cell => Split(cell.Bare(), available, mustProgress)).ToList();

        if (cuts.Any(cut => cut.Head is null))
        {
            return mustProgress ? new Cut(row, null) : new Cut(null, row);
        }

        if (cuts.All(cut => cut.Tail is null))
        {
            return new Cut(row, null);
        }

        float head = cuts.Max(cut => cut.Head!.Height);
        float tail = cuts.Max(cut => cut.Tail?.Height ?? 0);

        List<BlockFlow> heads = [.. cuts.Select(cut => Half((BlockFlow)cut.Head!, head, isSplitBelow: true))];
        List<BlockFlow> tails =
        [
            .. cuts.Select((cut, column) => Half(
                (cut.Tail as BlockFlow)?.Headed() ?? Continues(row.Cells[column]),
                tail,
                isSplitAbove: true)),
        ];

        return new Cut(
            row.With(heads, head, row.IsSplitAbove, isSplitBelow: true),
            row.With(tails, tail, isSplitAbove: true, row.IsSplitBelow));
    }

    /// <summary>One half of a cut cell: cut on the side the row was cut, and as tall as that half of the row.</summary>
    /// <remarks>
    /// Every cell of the row is marked, not only the one that overran. A cell whose content happened to fit
    /// is still half of a row that goes on, and left unmarked it would draw its own rule across the cut while
    /// the row itself drew none — a fence through the middle of the grid. The side is set before the height,
    /// because a cut side carries no border or padding and so changes what the height has to make room for.
    /// </remarks>
    private static BlockFlow Half(BlockFlow cell, float height, bool isSplitAbove = false, bool isSplitBelow = false) =>
        cell.With(cell.Children, 0, 0, isSplitAbove || cell.IsSplitAbove, isSplitBelow || cell.IsSplitBelow)
            .StretchedTo(height);

    /// <summary>What a column keeps on the continuation once its own content has run out.</summary>
    /// <remarks>
    /// Empty, unless the cell carries a heading to set again where it resumes. That is what a note in a margin
    /// column is: short beside something long, so it always runs out first, and the page it runs out on is
    /// exactly the page that needs to be told what it is looking at.
    /// </remarks>
    private static BlockFlow Continues(BlockFlow cell) =>
        new(cell.Frame with { MinContentHeight = null }, cell.Frame.Resumed, 0, 0, isSplitAbove: true, cell.IsSplitBelow);

    /// <summary>Cuts a block, and sets its continued foot on the fragment that turns out to have a continuation.</summary>
    /// <remarks>
    /// The cut is tried twice, and only where there is a foot to set. The first pass asks the plain question,
    /// whether the block fits: if it does, this is the last fragment and the foot says nothing, so it is left
    /// off and nothing has been spent. Only once the block is known to be cut is the pass run again with the
    /// foot's height held back, so the fragment keeps the room to set it. Reserving on the first pass instead
    /// would take that room from every table, and a table that fits exactly would be cut in order to make
    /// space for a sentence about a continuation that was never going to happen.
    /// <para>
    /// A foot with no room to stand in is left off altogether. Held back from a page it cannot fit on, the
    /// reserve turns the remaining room negative, and the rule that every page must make progress then places
    /// one row against it and moves on: measured, a foot of twenty lines on a page that holds fifteen drew a
    /// table of thirty rows out to sixteen pages, each carrying a promise of continuation and a single row.
    /// So the foot is weighed against the page first, and where it does not fit the plain cut stands and the
    /// document goes on unadorned. A foot that merely takes most of the page is set: that is the layout the
    /// markup asked for, and the pages come out as short as it made them.
    /// </para>
    /// </remarks>
    private static Cut SplitBlock(BlockFlow block, float available, bool mustProgress)
    {
        Cut cut = Divided(block, available, mustProgress, reserve: 0);

        if (cut.Tail is null || block.Frame.Continued.Count == 0)
        {
            return cut;
        }

        float foot = Stacking.Height(block.Frame.Continued);

        if (foot > available - block.Head)
        {
            return cut;
        }

        Cut footed = Divided(block, available, mustProgress, reserve: foot);

        return footed is { Head: BlockFlow { Children.Count: > 0 } head, Tail: not null }
            ? new Cut(head.Footed(), footed.Tail)
            : cut;
    }

    private static Cut Divided(BlockFlow block, float available, bool mustProgress, float reserve)
    {
        bool isTooTall = block.Height > available + Tolerance;

        if (isTooTall && !mustProgress && (block.Frame.AvoidsBreakInside || block.Children.Count == 0))
        {
            return new Cut(null, block);
        }

        IReadOnlyList<Flow> children = block.Children;
        int opening = Opening(children);
        float room = available - block.Head - reserve;
        float used = 0;
        float pending = 0;

        for (int index = 0; index < children.Count; index++)
        {
            Flow child = children[index];
            float gap = index == 0 ? child.MarginTop : Stacking.Collapse(pending, child.MarginTop);

            if (index > opening && child.ForcesBreakBefore)
            {
                return Divide(block, index, isForced: true);
            }

            float space = room - used - gap;

            if (child.Height > space + Tolerance || child.HasForcedBreakInside)
            {
                return BreakAt(block, index, space, mustProgress);
            }

            used += gap + child.Height;
            pending = child.MarginBottom;

            if (index < children.Count - 1 && child.ForcesBreakAfter)
            {
                return Divide(block, index + 1, isForced: true);
            }
        }

        return new Cut(block, null);
    }

    /// <summary>How many children a fragment opens with that it did not choose: the heading set again above it.</summary>
    /// <remarks>
    /// A forced break moves its child to the head of what follows, and the check that a break has already been
    /// honoured is that the child now stands first. Repeating a table heading above it breaks that: the row
    /// lands second, asks for a page again, and the document multiplies pages one heading at a time. Counting
    /// the heading out restores the reckoning for tables without changing it for anything else, which opens
    /// with nothing.
    /// </remarks>
    private static int Opening(IReadOnlyList<Flow> children)
    {
        int at = 0;

        while (at < children.Count && children[at] is RowFlow { IsHeader: true })
        {
            at++;
        }

        return at;
    }

    private static Cut BreakAt(BlockFlow block, int index, float space, bool mustProgress)
    {
        Cut cut = Split(block.Children[index], space, mustProgress && index == 0);

        if (cut.Head is not null)
        {
            return cut.Tail is null ? Divide(block, index + 1, isForced: false) : Divide(block, index, cut);
        }

        int preferred = block.Children[index] is LineFlow ? KeepLines(block, index) : Backtrack(block.Children, index);

        return (preferred, mustProgress) switch
        {
            ( > 0, _) => Divide(block, preferred, isForced: false),
            (_, false) => new Cut(null, block),
            _ => Divide(block, index, isForced: false),
        };
    }

    private static int KeepLines(BlockFlow block, int fitting)
    {
        int kept = Math.Min(fitting, block.Children.Count - block.Frame.Widows);

        return kept >= block.Frame.Orphans ? kept : 0;
    }

    private static int Backtrack(IReadOnlyList<Flow> children, int index)
    {
        int at = index;

        while (at > 0 && AvoidsBreakBetween(children[at - 1], children[at]))
        {
            at--;
        }

        return at;
    }

    /// <summary>Whether a page may not end between these two, so the break moves above them.</summary>
    /// <remarks>
    /// A table heading counts: left at the foot of a page it names columns that are not there, and the rows it
    /// belongs to open the next page bare. Backing up over it sends the heading down with its rows, and the
    /// page that has to make progress places it anyway.
    /// </remarks>
    private static bool AvoidsBreakBetween(Flow above, Flow below) =>
        above is BlockFlow { Frame.BreakAfter: BreakRule.Avoid } or RowFlow { IsHeader: true }
        || below is BlockFlow { Frame.BreakBefore: BreakRule.Avoid } or RowFlow { IsTied: true };

    private static Cut Divide(BlockFlow block, int at, bool isForced)
    {
        if (at >= block.Children.Count)
        {
            return new Cut(block, null);
        }

        IReadOnlyList<Flow> moved = Continued(block.Children, at);
        BlockFlow head = block.With([.. block.Children.Take(at)], block.MarginTop, 0, block.IsSplitAbove, isSplitBelow: true);
        BlockFlow tail = block.With(isForced ? moved : WithoutLeadingMargin(moved), 0, block.MarginBottom, isSplitAbove: true, block.IsSplitBelow);

        return new Cut(head, tail.Headed());
    }

    private static Cut Divide(BlockFlow block, int index, Cut inner)
    {
        IReadOnlyList<Flow> moved = [.. Heading(block.Children, index + 1), inner.Tail!, .. block.Children.Skip(index + 1)];
        BlockFlow head = block.With([.. block.Children.Take(index), inner.Head!], block.MarginTop, 0, block.IsSplitAbove, isSplitBelow: true);
        BlockFlow tail = block.With(moved, 0, block.MarginBottom, isSplitAbove: true, block.IsSplitBelow);

        return new Cut(head, tail.Headed());
    }

    /// <summary>What moves to the next page, with the header rows of a table repeated above it.</summary>
    /// <remarks>
    /// Only a table has header rows, and only when a row of its body stayed behind: repeating the head onto a
    /// page that took nothing else would grow the remainder instead of shrinking it, and the document would
    /// never reach its end.
    /// </remarks>
    private static IReadOnlyList<Flow> Continued(IReadOnlyList<Flow> children, int at) =>
        [.. Heading(children, at), .. children.Skip(at)];

    /// <summary>The header rows to set again above what moves, or none when repeating them would make no progress.</summary>
    private static IReadOnlyList<Flow> Heading(IReadOnlyList<Flow> children, int at)
    {
        IReadOnlyList<Flow> heading = [.. children.TakeWhile(child => child is RowFlow { IsHeader: true })];

        return heading.Count > 0 && at > heading.Count ? heading : [];
    }

    private static IReadOnlyList<Flow> WithoutLeadingMargin(IReadOnlyList<Flow> flows) =>
        flows is [BlockFlow first, ..] ? [WithoutTopMargin(first), .. flows.Skip(1)] : flows;

    private static BlockFlow WithoutTopMargin(BlockFlow block) =>
        block.With(
            block.Head == 0 ? WithoutLeadingMargin(block.Children) : block.Children,
            0,
            block.MarginBottom,
            block.IsSplitAbove,
            block.IsSplitBelow);

    private static Page Paint(Flow flow, float top, PageSetup setup, int number)
    {
        var sheet = new Sheet();

        if (setup.Background is { IsVisible: true } background)
        {
            sheet.Cover(setup, background);
        }

        sheet.Place(flow, top);

        return new Page(number, setup.Width, setup.Height, sheet.Boxes, sheet.Images, sheet.Spans);
    }

    private readonly record struct Cut(Flow? Head, Flow? Tail);
}
