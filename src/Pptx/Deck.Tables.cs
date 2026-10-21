using DocumentFormat.OpenXml.Packaging;
using Snail.Toolkit.HtmlComposer.Engine;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Snail.Toolkit.HtmlComposer.Pptx;

/// <summary>Tables as PowerPoint tables: a grid of columns, rows as tall as the page made them, cells holding paragraphs.</summary>
internal static partial class Deck
{
    /// <summary>PowerPoint's "No Style, No Grid": a table style that draws nothing of its own.</summary>
    private const string PlainTable = "{2D5ABB26-0587-4C30-8999-92F81FD0307C}";

    private const string TableUri = "http://schemas.openxmlformats.org/drawingml/2006/table";

    /// <summary>The size of the empty paragraph a cell without text must still hold, small enough never to set the row's height.</summary>
    private const int Empty = 100;

    /// <summary>A table fragment as a PowerPoint table of its own.</summary>
    /// <remarks>
    /// A table the page cut in two arrives as two fragments, each opening with the heading again, so on the
    /// next slide the table starts over with its head rather than carrying on headless. The rows keep the
    /// heights the page gave them; PowerPoint only ever makes a row taller than it is told, so a cell someone
    /// writes more into grows its row instead of overflowing it.
    /// </remarks>
    private static P.GraphicFrame TableOf(TableFrame frame, OpenXmlPart part, Ink ink, uint id)
    {
        var grid = new A.TableGrid();

        foreach (float column in frame.Columns)
        {
            grid.Append(new A.GridColumn { Width = Emu(column) });
        }

        var table = new A.Table(new A.TableProperties(new A.TableStyleId(PlainTable)), grid);

        foreach (TableRow row in frame.Rows)
        {
            table.Append(RowOf(row, frame.Columns.Count, part, ink));
        }

        return new P.GraphicFrame(
            new P.NonVisualGraphicFrameProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"table{id}" },
                new P.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.Transform(
                new A.Offset { X = Emu(frame.Bounds.X), Y = Emu(frame.Bounds.Y) },
                new A.Extents { Cx = Emu(frame.Bounds.Width), Cy = Emu(frame.Bounds.Height) }),
            new A.Graphic(new A.GraphicData(table) { Uri = TableUri }));
    }

    /// <summary>One row: a cell for every column, a spanning cell followed by the merged cells it covers.</summary>
    /// <remarks>
    /// A PowerPoint row has exactly as many cells as the grid has columns. A cell spanning three columns is
    /// written once with <c>gridSpan</c> and then twice more as merged placeholders, and a column no cell of the
    /// row reaches is given an empty cell so the row still lines up with its grid.
    /// </remarks>
    private static A.TableRow RowOf(TableRow row, int columns, OpenXmlPart part, Ink ink)
    {
        var written = new A.TableRow { Height = Emu(row.Height) };
        int next = 0;

        foreach (TableCell cell in row.Cells.OrderBy(cell => cell.Column))
        {
            while (next < cell.Column && next < columns)
            {
                written.Append(Vacant(default, default, isMerged: false));
                next++;
            }

            if (next >= columns)
            {
                break;
            }

            int span = Math.Min(cell.Span, columns - next);

            written.Append(CellOf(cell, span, part, ink));

            for (int merged = 1; merged < span; merged++)
            {
                written.Append(Vacant(cell.Top, cell.Bottom, isMerged: true));
            }

            next += span;
        }

        while (next < columns)
        {
            written.Append(Vacant(default, default, isMerged: false));
            next++;
        }

        return written;
    }

    private static A.TableCell CellOf(TableCell cell, int span, OpenXmlPart part, Ink ink)
    {
        var body = new A.TextBody(new A.BodyProperties(), new A.ListStyle());

        if (cell.Paragraphs.Count == 0)
        {
            body.Append(EmptyParagraph());
        }
        else
        {
            body.Append(Paragraphs(cell.Paragraphs, ink));
        }

        var properties = new A.TableCellProperties(
            Rule<A.LeftBorderLineProperties>(default),
            Rule<A.RightBorderLineProperties>(default),
            Rule<A.TopBorderLineProperties>(cell.Top),
            Rule<A.BottomBorderLineProperties>(cell.Bottom))
        {
            LeftMargin = (int)Emu(cell.Padding.Left),
            RightMargin = (int)Emu(cell.Padding.Right),
            TopMargin = (int)Emu(cell.Padding.Top),
            BottomMargin = (int)Emu(cell.Padding.Bottom),
            Anchor = cell.Alignment switch
            {
                CellAlignment.Middle => A.TextAnchoringTypeValues.Center,
                CellAlignment.Bottom => A.TextAnchoringTypeValues.Bottom,
                _ => A.TextAnchoringTypeValues.Top,
            },
        };

        properties.Append(FillOf(cell, part, ink));

        var written = new A.TableCell(body, properties);

        if (span > 1)
        {
            written.GridSpan = span;
        }

        return written;
    }

    /// <summary>What a cell is filled with: its picture, its colour, or nothing.</summary>
    private static DocumentFormat.OpenXml.OpenXmlElement FillOf(TableCell cell, OpenXmlPart part, Ink ink)
    {
        if (cell.Picture is { } picture)
        {
            return PictureFill(cell, picture, part, ink);
        }

        return cell.Background is { IsVisible: true } background ? new A.SolidFill(Colour(background)) : new A.NoFill();
    }

    /// <summary>A picture in a cell as the cell's own fill, standing where the page drew it inside the cell.</summary>
    /// <remarks>
    /// A picture laid over a table falls out of place as soon as a row above it grows; a fill moves with its
    /// cell, and PowerPoint changes it with "Format cell → Fill → Picture". The part of the picture that shows
    /// is the part the page showed, and the room around it — the cell's padding — is kept as the inset of the
    /// fill, so the picture keeps its own size inside a cell larger than it.
    /// </remarks>
    private static A.BlipFill PictureFill(TableCell cell, ImageBox picture, OpenXmlPart part, Ink ink)
    {
        var fill = new A.BlipFill(new A.Blip { Embed = ink.Pictures.IdOf(picture.Image, part) }) { Dpi = 0, RotateWithShape = true };

        fill.Append(picture.Source is { } source ? Cropped(source, picture.Image) : new A.SourceRectangle());

        Rect area = cell.Bounds;
        Rect drawn = picture.Bounds;

        fill.Append(new A.Stretch(new A.FillRectangle
        {
            Left = Share(drawn.X - area.X, area.Width),
            Top = Share(drawn.Y - area.Y, area.Height),
            Right = Share(area.Right - drawn.Right, area.Width),
            Bottom = Share(area.Bottom - drawn.Bottom, area.Height),
        }));

        return fill;
    }

    /// <summary>One side of a cell: the rule the page drew along that edge, or no line at all.</summary>
    private static T Rule<T>(BorderSide side)
        where T : A.LinePropertiesType, new()
    {
        if (!side.IsVisible)
        {
            var none = new T { Width = 0 };
            none.Append(new A.NoFill());

            return none;
        }

        var rule = new T
        {
            Width = (int)Emu(side.Width),
            CapType = A.LineCapValues.Flat,
            CompoundLineType = A.CompoundLineValues.Single,
        };

        rule.Append(new A.SolidFill(Colour(side.Color)));
        rule.Append(new A.PresetDash
        {
            Val = side.Style switch
            {
                BorderStyle.Dashed => A.PresetLineDashValues.Dash,
                BorderStyle.Dotted => A.PresetLineDashValues.SystemDot,
                _ => A.PresetLineDashValues.Solid,
            },
        });

        return rule;
    }

    /// <summary>A cell with nothing of its own: a column the row does not reach, or one a spanning cell covers.</summary>
    /// <remarks>
    /// A covered cell repeats the rules of the cell covering it, because PowerPoint draws the edge of every grid
    /// column from the cell written there, merged or not: left without them, a rule under a spanning cell
    /// would stop at the end of its first column.
    /// </remarks>
    private static A.TableCell Vacant(BorderSide top, BorderSide bottom, bool isMerged)
    {
        var cell = new A.TableCell(
            new A.TextBody(new A.BodyProperties(), new A.ListStyle(), EmptyParagraph()),
            new A.TableCellProperties(
                Rule<A.LeftBorderLineProperties>(default),
                Rule<A.RightBorderLineProperties>(default),
                Rule<A.TopBorderLineProperties>(top),
                Rule<A.BottomBorderLineProperties>(bottom),
                new A.NoFill()));

        if (isMerged)
        {
            cell.HorizontalMerge = true;
        }

        return cell;
    }

    private static A.Paragraph EmptyParagraph() => new(new A.EndParagraphRunProperties { FontSize = Empty, Dirty = false });
}
