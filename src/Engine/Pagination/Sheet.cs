using Snail.Toolkit.HtmlComposer.Engine.Layout;

namespace Snail.Toolkit.HtmlComposer.Engine.Pagination;

/// <summary>Collects what one page paints, in the order it is painted.</summary>
internal sealed class Sheet
{
    public List<RenderBox> Boxes { get; } = [];

    public List<ImageBox> Images { get; } = [];

    public List<TextSpan> Spans { get; } = [];

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
        BoxOf(block.Frame, top, block.Height, block.IsSplitAbove, block.IsSplitBelow);

    private static RenderBox BoxOf(LineMark mark, float top) =>
        new(
            BoxKind.Marker,
            new Rect(mark.X, top + mark.Top, mark.Size, mark.Size),
            mark.Fill,
            new Border(mark.Outline, mark.Outline, mark.Outline, mark.Outline),
            Radius: mark.Radius);

    private void PlaceBlock(BlockFlow block, float top)
    {
        if (!block.Frame.IsAnonymous)
        {
            Boxes.Add(BoxOf(block, top));
        }

        IReadOnlyList<float> offsets = Stacking.Offsets(block.Children);

        for (int index = 0; index < block.Children.Count; index++)
        {
            Place(block.Children[index], top + block.Head + offsets[index]);
        }
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

    private void PlaceLine(LineFlow line, float top)
    {
        Boxes.AddRange(line.Boxes.Select(box => new RenderBox(
            BoxKind.Inline,
            new Rect(box.X, top + box.Top, box.Width, box.Height),
            box.Fill,
            box.Border,
            Radius: box.Radius)));

        Boxes.AddRange(line.Marks.Select(mark => BoxOf(mark, top)));

        Spans.AddRange(line.Texts.Select(text =>
            new TextSpan(text.Text, text.X, top + text.Baseline, text.Width, text.Font, text.Color, text.Decoration)));

        Images.AddRange(line.Pictures.Select(picture =>
            new ImageBox(new Rect(picture.X, top + picture.Top, picture.Width, picture.Height), picture.Image, picture.Source)));
    }
}
