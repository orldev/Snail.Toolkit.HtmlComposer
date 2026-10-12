using System.Globalization;
using AngleSharp.Dom;
using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>Sets a styled document into one long column of blocks and lines, before it is cut into pages.</summary>
/// <remarks>
/// Named after the typesetter's galley, the tray type was set into before pages were made up from it. Widths
/// are final here and heights are measured, but nothing knows yet where a page ends. That separation is what
/// lets pagination move a line to the next page without setting it again.
/// </remarks>
internal sealed class Galley
{
    private readonly Cascade _cascade;

    private readonly LineBreaker _breaker;

    private readonly Tables _tables;

    private readonly IImageSource? _images;

    private readonly List<Note> _notes;

    private readonly Dictionary<string, Image?> _pictures = new(StringComparer.Ordinal);

    private readonly float _pageHeight;

    private readonly CancellationToken _ct;

    private float _rootFontSize = Style.Initial.Font.Size;

    private Galley(Cascade cascade, Measurements measurements, IImageSource? images, List<Note> notes, float pageHeight, CancellationToken ct)
    {
        _cascade = cascade;
        _breaker = new LineBreaker(measurements, ct);
        _tables = new Tables(this);
        _images = images;
        _notes = notes;
        _pageHeight = pageHeight;
        _ct = ct;
    }

    /// <summary>The token the layout looks at, so work that runs long stops when the host gives up on it.</summary>
    internal CancellationToken Token => _ct;

    /// <summary>Records one thing that could not be set as written.</summary>
    internal void Remark(Note note) => _notes.Add(note);

    /// <summary>Sets a document into one column as wide as the content area, with the cascade of its own sheets unless one is given.</summary>
    /// <remarks>
    /// A cascade is given when the same document is set again and again, as a running line is on every page:
    /// its rules and its decisions hold as long as the elements do, and only the text between them changes.
    /// </remarks>
    public static BlockFlow Set(
        IDocument document,
        Rect content,
        Measurements measurements,
        IImageSource? images,
        List<Note> notes,
        CancellationToken ct,
        Cascade? cascade = null)
    {
        var galley = new Galley(cascade ?? Cascade.Of(document), measurements, images, notes, content.Height, ct);

        return galley.Root(document.DocumentElement, content);
    }

    private static float? ContentHeightOf(Style style, Edges padding, Edges borders) =>
        style.Height is { Percent: 0 } height
            ? Math.Max(0, height.Points - (style.IsBorderBox ? padding.Vertical + borders.Vertical : 0))
            : null;

    private static BlockFlow Collapsed(BlockFrame frame, List<Flow> children, float marginTop, float marginBottom)
    {
        bool hasHead = frame.Border.Top.Width + frame.Padding.Top > 0;
        bool hasFoot = frame.Border.Bottom.Width + frame.Padding.Bottom > 0 || frame.MinContentHeight is not null;

        if (!hasHead && children is [BlockFlow first, ..])
        {
            marginTop = Stacking.Collapse(marginTop, first.MarginTop);
            children[0] = first.With(first.Children, 0, first.MarginBottom, isSplitAbove: false, isSplitBelow: false);
        }

        if (!hasFoot && children is [.., BlockFlow last])
        {
            marginBottom = Stacking.Collapse(marginBottom, last.MarginBottom);
            children[^1] = last.With(last.Children, last.MarginTop, 0, isSplitAbove: false, isSplitBelow: false);
        }

        if (!hasHead && !hasFoot && children.Count == 0)
        {
            return new BlockFlow(frame, children, Stacking.Collapse(marginTop, marginBottom), 0);
        }

        return new BlockFlow(frame, children, marginTop, marginBottom);
    }

    /// <summary>A block of the given content, framed by its border box, with its margins collapsed through it.</summary>
    internal static BlockFlow Framed(IElement element, Style style, BorderBox box, List<Flow> children, float containingWidth)
    {
        var frame = new BlockFrame(
            element.LocalName,
            box.X,
            box.Width,
            style.Border,
            box.Padding,
            style.Background,
            style.BreakBefore,
            style.BreakAfter,
            style.AvoidsBreakInside,
            style.Orphans,
            style.Widows,
            ContentHeightOf(style, box.Padding, box.Borders),
            style.BorderRadius)
        {
            ClonesDecoration = style.ClonesDecoration,
        };

        return Collapsed(frame, children, style.Margin.Top?.Resolve(containingWidth) ?? 0, style.Margin.Bottom?.Resolve(containingWidth) ?? 0);
    }

    private static float? PixelsOf(IElement element, string attribute) =>
        float.TryParse(element.GetAttribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out float pixels) && pixels > 0
            ? Points.FromPixels(pixels)
            : null;

    internal static bool IsTag(IElement element, string name) => element.LocalName.Equals(name, StringComparison.Ordinal);

    private static string Transformed(string text, TextTransform transform) => transform switch
    {
        TextTransform.Uppercase => text.ToUpperInvariant(),
        TextTransform.Lowercase => text.ToLowerInvariant(),
        TextTransform.Capitalize => Capitalized(text),
        _ => text,
    };

    /// <summary>Raises the first letter of every word, leaving the rest of each word as it was typed.</summary>
    /// <remarks>
    /// Casing is invariant rather than cultural: a document carries no locale, and a Turkish machine would
    /// otherwise print a dotless capital in English and Russian text alike.
    /// </remarks>
    private static string Capitalized(string text)
    {
        char[] letters = text.ToCharArray();
        bool isWordStart = true;

        for (int index = 0; index < letters.Length; index++)
        {
            letters[index] = isWordStart ? char.ToUpperInvariant(letters[index]) : letters[index];
            isWordStart = !char.IsLetterOrDigit(letters[index]);
        }

        return new string(letters);
    }

    private BlockFlow Root(IElement root, Rect content)
    {
        Style style = Compute(root, Style.Initial) with { Display = Display.Block };
        _rootFontSize = style.Font.Size;

        return Block(root, style, content.X, content.Width);
    }

    /// <summary>Sets a block, or a list item whose marker the first line of its content is to hang.</summary>
    /// <remarks>
    /// A list item opens a marker of its own; any other block hands on the one it was given, so the marker
    /// reaches the first line set inside the item however deep that line is.
    /// </remarks>
    internal BlockFlow Block(IElement element, Style style, float left, float containingWidth, PendingMarker? pending = null)
    {
        var box = BorderBox.Of(style, left, containingWidth);
        PendingMarker? marker = style.Display == Display.ListItem ? new PendingMarker(Numbering.MarkerOf(element, style)) : pending;
        List<Flow> children = Content(element, style, box.ContentLeft, box.ContentWidth, marker);
        BlockFlow block = Framed(element, style, box, children, containingWidth);
        List<Flow> resumed = Resumed(element, style, box);

        return resumed.Count == 0 ? block : block.Resuming(resumed);
    }

    /// <summary>What the block sets again wherever it carries on, laid out as the block's own content is.</summary>
    /// <remarks>
    /// An <c>aside</c> marked <c>resumed</c> is the heading a reader meets on the second page of something
    /// that began on the first — "continued", the positions this page covers, the name of the table. It is
    /// hidden in the flow by the user agent sheet and set here instead, so it costs the first page nothing and
    /// stands at the top of every later one. Only the block's own children are looked at: an aside belongs to
    /// the block that carries it, and one nested deeper is that block's business, not this one's.
    /// </remarks>
    private List<Flow> Resumed(IElement element, Style style, BorderBox box)
    {
        List<Flow> resumed = [];

        foreach (IElement child in element.Children.Where(child => IsTag(child, "aside") && child.HasAttribute("resumed")))
        {
            Style inner = Compute(child, style) with { Display = Display.Block };

            resumed.Add(Block(child, inner, box.ContentLeft, box.ContentWidth));
        }

        return resumed;
    }

    /// <summary>The computed style of an element under its parent's.</summary>
    internal Style Compute(IElement element, Style parent) => Styles.Compute(_cascade.Specified(element), parent, _rootFontSize);

    /// <summary>The narrowest and the widest an element's content can be set, its own padding and borders included.</summary>
    /// <remarks>
    /// Measuring the content is what automatic table layout costs: a cell has to say what it wants before the
    /// column can hand it a width, so its text is tokenised once here and set for real once the column is
    /// settled. Blocks inside take the widest of their children, because blocks stack rather than sit side by side.
    /// </remarks>
    internal (float Min, float Max) Intrinsic(IElement element, Style style, float basis)
    {
        _ct.ThrowIfCancellationRequested();

        List<InlineItem> run = [];
        List<(float Min, float Max)> nested = [];

        if (IsTag(element, "img"))
        {
            Append(run, Picture(element, style, basis));
        }
        else
        {
            Gather(element, style, run, nested, basis);
        }

        (float min, float max) = _breaker.Intrinsic(run);
        float chrome = style.Padding.Resolve(basis).Horizontal + style.Border.Widths.Horizontal;

        return (
            nested.Aggregate(min, (least, child) => Math.Max(least, child.Min)) + chrome,
            nested.Aggregate(max, (widest, child) => Math.Max(widest, child.Max)) + chrome);
    }

    private void Gather(INode node, Style parent, List<InlineItem> run, List<(float Min, float Max)> nested, float basis)
    {
        foreach (INode child in node.ChildNodes)
        {
            _ct.ThrowIfCancellationRequested();

            if (child is IText text)
            {
                run.Add(new TextRun(Transformed(text.Data, parent.Transform), parent));
                continue;
            }

            if (child is not IElement element
                || Compute(element, parent) is not { Display: not Display.None } style)
            {
                continue;
            }

            if (IsTag(element, "br"))
            {
                run.Add(new LineBreak(style));
                continue;
            }

            if (IsTag(element, "img"))
            {
                Append(run, Picture(element, style, basis));
                continue;
            }

            if (IsTag(element, "table"))
            {
                float table = _tables.Width(element, style, basis);

                nested.Add((table, table));
                continue;
            }

            if (style.IsBlockLevel)
            {
                nested.Add(Intrinsic(element, style, basis));
                continue;
            }

            Gather(element, style, run, nested, basis);
        }
    }

    private static void Append(List<InlineItem> run, InlineItem? item)
    {
        if (item is not null)
        {
            run.Add(item);
        }
    }

    /// <summary>The content of one block: its inline runs and the blocks nested in it.</summary>
    /// <remarks>
    /// The style handed to the inline children is stripped of the background and the border, because the block
    /// already paints those through its own box. A text node has no style of its own and carries its parent's,
    /// so without stripping, text sitting straight inside a bordered cell would be framed letter by letter.
    /// Inline elements within are unaffected: neither property is inherited, so each is computed afresh from
    /// what that element itself declared.
    /// </remarks>
    private List<Flow> Content(IElement element, Style style, float x, float width, PendingMarker? pending)
    {
        Style inner = style with { Background = null, Border = Border.None, BorderRadius = 0 };
        var segments = new Segments(this, inner, x, width, pending);

        if (IsTag(element, "img"))
        {
            segments.Add(Picture(element, inner, width));

            return segments.Close();
        }

        Walk(element, inner, segments);

        return segments.Close();
    }

    private void Walk(INode node, Style parent, Segments segments)
    {
        foreach (INode child in node.ChildNodes)
        {
            _ct.ThrowIfCancellationRequested();

            if (child is IText text)
            {
                segments.Add(new TextRun(Transformed(text.Data, parent.Transform), parent));
                continue;
            }

            if (child is IElement element && Compute(element, parent) is { Display: not Display.None } style)
            {
                Visit(element, style, segments);
            }
        }
    }

    private void Visit(IElement element, Style style, Segments segments)
    {
        if (IsTag(element, "br"))
        {
            segments.Add(new LineBreak(style));
            return;
        }

        if (style.IsBlockLevel)
        {
            segments.Flush();
            segments.Add(IsTag(element, "table")
                ? _tables.Set(element, style, segments.X, segments.Width, segments.Pending)
                : Block(element, style, segments.X, segments.Width, segments.Pending));

            return;
        }

        if (IsTag(element, "img"))
        {
            segments.Add(Picture(element, style, segments.Width));
            return;
        }

        float before = style.Margin.Left?.Resolve(segments.Width) ?? 0;
        float after = style.Margin.Right?.Resolve(segments.Width) ?? 0;

        segments.Add(before > 0 ? new InlineSpace(before, style) : null);
        Walk(element, style, segments);
        segments.Add(after > 0 ? new InlineSpace(after, style) : null);
    }

    private InlineItem? Picture(IElement element, Style style, float containingWidth)
    {
        string source = element.GetAttribute("src") ?? "";
        Image? image = Read(source);

        if (image is not { Width: > 0, Height: > 0 })
        {
            _notes.Add(MissingPicture(source));

            return element.GetAttribute("alt") is { Length: > 0 } alternative ? new TextRun(alternative, style) : null;
        }

        (float width, float height, Rect? crop) = SizeOf(image, element, style, containingWidth);

        return new PictureRun(
            image,
            width,
            height,
            style,
            style.Margin.Left?.Resolve(containingWidth) ?? 0,
            style.Margin.Right?.Resolve(containingWidth) ?? 0,
            crop);
    }

    private (float Width, float Height, Rect? Source) SizeOf(Image image, IElement element, Style style, float containingWidth)
    {
        float ratio = (float)image.Height / image.Width;
        float? declaredWidth = style.Width?.Resolve(containingWidth) ?? PixelsOf(element, "width");
        float? declaredHeight = style.Height is { Percent: 0 } height ? height.Points : PixelsOf(element, "height");

        (float naturalWidth, float naturalHeight) = (declaredWidth, declaredHeight) switch
        {
            ({ } fixedWidth, { } fixedHeight) => (fixedWidth, fixedHeight),
            ({ } fixedWidth, null) => (fixedWidth, fixedWidth * ratio),
            (null, { } fixedHeight) => (fixedHeight / ratio, fixedHeight),
            _ => (Points.FromPixels(image.Width), Points.FromPixels(image.Height)),
        };

        float widest = Math.Min(containingWidth, style.MaxWidth?.Resolve(containingWidth) ?? float.MaxValue);
        float scale = Math.Min(1, Math.Min(widest / naturalWidth, _pageHeight / naturalHeight));
        float drawnWidth = naturalWidth * scale;
        float drawnHeight = naturalHeight * scale;
        bool isFramed = declaredWidth is not null && declaredHeight is not null;

        if (!isFramed)
        {
            return (drawnWidth, drawnHeight, null);
        }

        return style.ObjectFit switch
        {
            ObjectFit.Cover => (drawnWidth, drawnHeight, Cropped(image, drawnHeight / drawnWidth)),
            ObjectFit.Contain => Fitted(drawnWidth, drawnHeight, ratio),
            _ => (drawnWidth, drawnHeight, null),
        };
    }

    /// <summary>The middle of a picture, as wide and as tall as the frame it has to fill.</summary>
    /// <remarks>
    /// <c>object-fit: cover</c> keeps the frame and gives up the parts of the picture that do not fit. The cut
    /// is centred because that is where the subject of a photograph usually is, and because CSS says so.
    /// </remarks>
    private static Rect Cropped(Image image, float frame)
    {
        float wanted = image.Width * frame;

        if (wanted <= image.Height)
        {
            return new Rect(0, (image.Height - wanted) / 2, image.Width, wanted);
        }

        float across = image.Height / frame;

        return new Rect((image.Width - across) / 2, 0, across, image.Height);
    }

    /// <summary>The largest the whole picture can be drawn inside a frame without changing its shape.</summary>
    private static (float Width, float Height, Rect? Source) Fitted(float width, float height, float ratio)
    {
        float across = Math.Min(width, height / ratio);

        return (across, across * ratio, null);
    }

    /// <summary>Reads a picture once per document, however many times the layout asks about it.</summary>
    /// <remarks>
    /// A cell of a table is measured before it is set, so every picture in one is looked up twice. Reading it
    /// twice would mean decoding the file twice and, for a picture that cannot be read, saying so twice.
    /// </remarks>
    private Image? Read(string source)
    {
        if (_pictures.TryGetValue(source, out Image? cached))
        {
            return cached;
        }

        Image? image = source.Length > 0 ? _images?.Load(source) : null;
        _pictures[source] = image;

        return image;
    }

    private Note MissingPicture(string source)
    {
        string shown = source.Length > 80 ? $"{source[..77]}..." : source;
        string text = _images is null
            ? $"Image \"{shown}\" was left out: the typesetter was given no image source."
            : $"Image \"{shown}\" was left out: it could not be read.";

        return new Note(NoteKind.MissingPicture, shown, text);
    }

    /// <summary>The content of one block: runs of lines interleaved with the blocks nested in it.</summary>
    private sealed class Segments(Galley galley, Style style, float x, float width, PendingMarker? pending)
    {
        private readonly List<InlineItem> _inline = [];

        private readonly List<Flow> _flows = [];

        public float X => x;

        public float Width => width;

        /// <summary>The marker still waiting for the item's first line, handed on to the blocks nested here.</summary>
        public PendingMarker? Pending => pending;

        public void Add(InlineItem? item)
        {
            if (item is not null)
            {
                _inline.Add(item);
            }
        }

        public void Add(BlockFlow block) => _flows.Add(block);

        /// <summary>Sets the inline content gathered so far into lines.</summary>
        /// <remarks>
        /// Runs before a nested block is laid out, never after it: a nested list hands out markers to its own
        /// items, and lines set afterwards would find this item's marker already taken.
        /// </remarks>
        public void Flush()
        {
            if (_inline.Any(IsVisible))
            {
                IReadOnlyList<LineFlow> lines = galley._breaker.Break([.. _inline], style, x, width, pending?.Take());
                var frame = new BlockFrame("", x, width, Border.None, default, null, BreakRule.Auto, BreakRule.Auto, false, style.Orphans, style.Widows, null);
                _flows.Add(new BlockFlow(frame, [.. lines], 0, 0));
            }

            _inline.Clear();
        }

        public List<Flow> Close()
        {
            Flush();

            return _flows is [BlockFlow { Frame.IsAnonymous: true } only] ? [.. only.Children] : _flows;
        }

        private static bool IsVisible(InlineItem item) =>
            item is not TextRun run
            || run.Style.WhiteSpace is not (WhiteSpace.Normal or WhiteSpace.NoWrap)
            || run.Text.Any(character => character is not (' ' or '\t' or '\n' or '\r' or '\f'));
    }
}
