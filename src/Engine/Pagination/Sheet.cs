using Snail.Toolkit.HtmlComposer.Engine.Layout;

namespace Snail.Toolkit.HtmlComposer.Engine.Pagination;

/// <summary>Collects what one page paints, in the order it is painted, and what its text and tables are made of.</summary>
/// <remarks>
/// The paint lists are all a fixed page needs. The structure — which runs make a paragraph, which boxes make a
/// table — is gathered on the same walk, because only here are both the flows and the objects made from them
/// in hand: a renderer that lets text flow again is told which of the objects it was given belong together.
/// </remarks>
internal sealed class Sheet(bool isStructured)
{
    public List<RenderBox> Boxes { get; } = [];

    public List<ImageBox> Images { get; } = [];

    public List<TextSpan> Spans { get; } = [];

    public List<TextFrame> Texts { get; } = [];

    public List<TableFrame> Tables { get; } = [];

    /// <summary>Paints the whole sheet, edge to edge, beneath everything set on it.</summary>
    public void Cover(PageSetup setup, Rgba background) =>
        Boxes.Add(new RenderBox(BoxKind.Sheet, new Rect(0, 0, setup.Width, setup.Height), background, Border.None));

    public void Place(Flow flow, float top)
    {
        if (flow is LineFlow line)
        {
            PlaceLine(line, top);
            return;
        }

        if (flow is RowFlow row)
        {
            PlaceRow(row, top);
            return;
        }

        if (flow is BlockFlow block)
        {
            PlaceBlock(block, top);
        }
    }

    private static RenderBox BoxOf(BlockFrame frame, float top, float height, bool isSplitAbove, bool isSplitBelow)
    {
        bool isWhole = !isSplitAbove && !isSplitBelow;
        Border border = frame.Border with
        {
            Top = isSplitAbove ? default : frame.Border.Top,
            Bottom = isSplitBelow ? default : frame.Border.Bottom,
        };

        return new RenderBox(
            BoxKind.Element,
            new Rect(frame.X, top, frame.Width, height),
            frame.Background,
            border,
            isSplitAbove,
            isSplitBelow,
            isWhole ? frame.Radius : 0,
            frame.Element);
    }

    private static RenderBox BoxOf(BlockFlow block, float top) =>
        BoxOf(
            block.Frame,
            top,
            block.Height,
            block.IsSplitAbove && !block.Frame.ClonesDecoration,
            block.IsSplitBelow && !block.Frame.ClonesDecoration);

    private static RenderBox BoxOf(LineMark mark, float top) =>
        new(
            BoxKind.Marker,
            new Rect(mark.X, top + mark.Top, mark.Size, mark.Size),
            mark.Fill,
            new Border(mark.Outline, mark.Outline, mark.Outline, mark.Outline),
            Radius: mark.Radius);

    /// <summary>Where the lines of a block may run: inside its border and padding.</summary>
    private static (float Left, float Width) ContentOf(BlockFrame frame)
    {
        float left = frame.X + frame.Border.Left.Width + frame.Padding.Left;
        float right = frame.X + frame.Width - frame.Border.Right.Width - frame.Padding.Right;

        return (left, Math.Max(0, right - left));
    }

    private void PlaceBlock(BlockFlow block, float top)
    {
        if (isStructured && Grid.IsGrid(block))
        {
            PlaceTable(block, top);
            return;
        }

        RenderBox? box = block.Frame.IsAnonymous ? null : BoxOf(block, top);

        if (box is not null)
        {
            Boxes.Add(box);
        }

        IReadOnlyList<float> offsets = Stacking.Offsets(block.Children);
        (float left, float width) = ContentOf(block.Frame);
        Writing? writing = isStructured ? new Writing(Spans) : null;

        for (int index = 0; index < block.Children.Count; index++)
        {
            Flow child = block.Children[index];
            float at = top + block.Head + offsets[index];

            if (child is LineFlow line)
            {
                int added = PlaceLine(line, at);
                writing?.Add(line, at, added);
                continue;
            }

            Frame(writing, left, width);
            Place(child, at);
        }

        Frame(writing, left, width);
    }

    /// <summary>Gives the lines gathered so far a frame of their own, if they can be given back as text.</summary>
    private void Frame(Writing? writing, float left, float width)
    {
        if (writing is null)
        {
            return;
        }

        bool isPlain = writing.IsPlain;

        if (writing.Close() is not { Count: > 0 } paragraphs || !isPlain)
        {
            return;
        }

        float first = paragraphs[0].Lines[0].Top;
        TextLine last = paragraphs[^1].Lines[^1];

        Texts.Add(new TextFrame(new Rect(left, first, width, last.Top + last.Height - first), paragraphs));
    }

    private void PlaceRow(RowFlow row, float top)
    {
        if (!row.Frame.IsAnonymous)
        {
            Boxes.Add(BoxOf(row.Frame, top, row.Height, row.IsSplitAbove, row.IsSplitBelow));
        }

        foreach (BlockFlow cell in row.Cells)
        {
            PlaceBlock(cell, top);
        }
    }

    /// <summary>Places a table that is a plain grid, and keeps it as one: its rows, its cells and what each holds.</summary>
    private void PlaceTable(BlockFlow table, float top)
    {
        var grid = new Grid(table);

        if (!table.Frame.IsAnonymous)
        {
            grid.Own(Add(BoxOf(table, top)));
        }

        IReadOnlyList<float> offsets = Stacking.Offsets(table.Children);

        for (int index = 0; index < table.Children.Count; index++)
        {
            var row = (RowFlow)table.Children[index];
            float at = top + table.Head + offsets[index];

            if (!row.Frame.IsAnonymous)
            {
                grid.Own(Add(BoxOf(row.Frame, at, row.Height, row.IsSplitAbove, row.IsSplitBelow)));
            }

            grid.Open(row, at);

            foreach (BlockFlow cell in row.Cells)
            {
                RenderBox? box = cell.Frame.IsAnonymous ? null : Add(BoxOf(cell, at));
                int boxes = Boxes.Count;
                int pictures = Images.Count;
                var writing = new Writing(Spans);

                Write(cell, at, writing);

                grid.Add(cell, box, writing.Close(), Boxes.GetRange(boxes, Boxes.Count - boxes), Images.GetRange(pictures, Images.Count - pictures));
            }
        }

        Tables.Add(grid.Close());
    }

    /// <summary>Places what a cell holds, its lines going into one run of paragraphs however deep they sit.</summary>
    private void Write(BlockFlow block, float top, Writing writing)
    {
        IReadOnlyList<float> offsets = Stacking.Offsets(block.Children);

        for (int index = 0; index < block.Children.Count; index++)
        {
            Flow child = block.Children[index];
            float at = top + block.Head + offsets[index];

            if (child is LineFlow line)
            {
                writing.Add(line, at, PlaceLine(line, at));
                continue;
            }

            if (child is BlockFlow inner)
            {
                if (!inner.Frame.IsAnonymous)
                {
                    Boxes.Add(BoxOf(inner, at));
                }

                Write(inner, at, writing);
                continue;
            }

            Place(child, at);
        }
    }

    private RenderBox Add(RenderBox box)
    {
        Boxes.Add(box);

        return box;
    }

    /// <summary>Puts one line on the page: what is painted behind it, its markers, its text and its pictures.</summary>
    /// <returns>How many runs the line added, so the caller can tell its own runs from the rest of the page.</returns>
    /// <remarks>Index loops rather than four queries, because every line of every page comes through here.</remarks>
    private int PlaceLine(LineFlow line, float top)
    {
        for (int index = 0; index < line.Boxes.Count; index++)
        {
            LineBox box = line.Boxes[index];
            Boxes.Add(new RenderBox(BoxKind.Inline, new Rect(box.X, top + box.Top, box.Width, box.Height), box.Fill, box.Border, Radius: box.Radius));
        }

        for (int index = 0; index < line.Marks.Count; index++)
        {
            Boxes.Add(BoxOf(line.Marks[index], top));
        }

        for (int index = 0; index < line.Texts.Count; index++)
        {
            LineText text = line.Texts[index];
            Spans.Add(new TextSpan(text.Text, text.X, top + text.Baseline, text.Width, text.Font, text.Color, text.Decoration) { Field = text.Field });
        }

        for (int index = 0; index < line.Pictures.Count; index++)
        {
            LinePicture picture = line.Pictures[index];
            Images.Add(new ImageBox(new Rect(picture.X, top + picture.Top, picture.Width, picture.Height), picture.Image, picture.Source));
        }

        return line.Texts.Count;
    }

    /// <summary>Lines gathered into paragraphs as they are placed: a paragraph ends where its block or a forced break ends it.</summary>
    /// <remarks>
    /// A list marker is left out: it hangs beside the paragraph, not in it. The marker says what it is rather than
    /// being guessed from where it stands — a first line hung to the left by a negative <c>text-indent</c> stands
    /// there too, and taking it for a marker tore its words out of the paragraph. Lines carrying pictures or painted inline boxes cannot be given back as plain text,
    /// and the writing says so, so that their runs keep the places they were drawn in.
    /// <para>
    /// Every block of every page passes through one of these, most of them holding no line at all, so the lists
    /// are made only once a line arrives, and closing hands them over and starts afresh rather than asking for a
    /// new writing after every block between the lines.
    /// </para>
    /// </remarks>
    private sealed class Writing(List<TextSpan> placed)
    {
        private List<TextParagraph>? _paragraphs;

        private List<TextLine>? _lines;

        private TextAlignment _alignment;

        public bool IsPlain { get; private set; } = true;

        public void Add(LineFlow line, float top, int added)
        {
            IsPlain &= line.Pictures.Count == 0 && line.Boxes.Count == 0;

            List<TextSpan> spans = new(added);
            int first = placed.Count - added;

            for (int index = 0; index < added; index++)
            {
                if (!line.Texts[index].IsMarker)
                {
                    spans.Add(placed[first + index]);
                }
            }

            _lines ??= [];

            if (_lines.Count == 0)
            {
                _alignment = Alignment(line.Align);
            }

            if (spans.Count > 0)
            {
                _lines.Add(new TextLine(top, line.Height, spans, line.End == LineEnd.Space));
            }

            if (line.End is LineEnd.Forced or LineEnd.Last)
            {
                End();
            }
        }

        public IReadOnlyList<TextParagraph> Close()
        {
            End();

            IReadOnlyList<TextParagraph> paragraphs = _paragraphs ?? [];

            _paragraphs = null;
            IsPlain = true;

            return paragraphs;
        }

        private void End()
        {
            if (_lines is not { Count: > 0 })
            {
                return;
            }

            _paragraphs ??= [];
            _paragraphs.Add(new TextParagraph(_alignment, [.. _lines]));
            _lines.Clear();
        }

        private static TextAlignment Alignment(Styling.TextAlign align) => align switch
        {
            Styling.TextAlign.Right => TextAlignment.Right,
            Styling.TextAlign.Center => TextAlignment.Center,
            Styling.TextAlign.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left,
        };
    }
}
