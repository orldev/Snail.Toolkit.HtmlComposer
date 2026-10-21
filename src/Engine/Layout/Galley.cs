using System.Globalization;
using System.Text;
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
    /// <summary>How deep elements may nest before what lies below is set as plain text.</summary>
    /// <remarks>
    /// The layout follows the tree by recursion, a few frames per element, and a stack overflow is not an
    /// exception: it ends the process. Measured on a thread-pool thread, 1 000 nested blocks were set and 2 000 —
    /// ten kilobytes of markup — ended a web server with them. A document nests a few dozen deep; this leaves room
    /// for ten times that, and below it the words are kept and only the boxes are given up.
    /// </remarks>
    internal const int Deepest = 256;

    private readonly Cascade _cascade;

    private readonly LineBreaker _breaker;

    private readonly Tables _tables;

    private readonly Pictures _pictures;

    private readonly List<Note> _notes;

    private readonly float _pageHeight;

    private readonly CancellationToken _ct;

    private float _rootFontSize = Style.Initial.Font.Size;

    private bool _isWordStart = true;

    private int _depth;

    private bool _isCapped;

    private Galley(Cascade cascade, Measurements measurements, Pictures pictures, List<Note> notes, float pageHeight, CancellationToken ct)
    {
        _cascade = cascade;
        _breaker = new LineBreaker(measurements, ct);
        _tables = new Tables(this);
        _pictures = pictures;
        _notes = notes;
        _pageHeight = pageHeight;
        _ct = ct;
    }

    /// <summary>The token the layout looks at, so work that runs long stops when the host gives up on it.</summary>
    internal CancellationToken Token => _ct;

    /// <summary>Records one thing that could not be set as written.</summary>
    internal void Remark(Note note) => _notes.Add(note);

    /// <summary>Sets a document into one column as wide as the content area, styled by the given cascade.</summary>
    /// <remarks>
    /// The cascade is kept by a caller that sets the same document again and again, as a running line is set on
    /// every page: its rules and its decisions hold as long as the elements do, and only the text between them
    /// changes.
    /// </remarks>
    public static BlockFlow Set(
        IDocument document,
        Cascade cascade,
        Rect content,
        Measurements measurements,
        Pictures pictures,
        List<Note> notes,
        CancellationToken ct)
    {
        var galley = new Galley(cascade, measurements, pictures, notes, content.Height, ct);

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
        float.TryParse(element.GetAttribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out float pixels)
        && pixels > 0
        && float.IsFinite(Points.FromPixels(pixels))
            ? Math.Min(Points.FromPixels(pixels), CssValues.Largest)
            : null;

    internal static bool IsTag(IElement element, string name) => element.LocalName.Equals(name, StringComparison.Ordinal);

    /// <summary>A text as its transform writes it, the start of a word known across the texts before it.</summary>
    /// <remarks>
    /// A word may be split between nodes — <c>&lt;b&gt;Hel&lt;/b&gt;lo</c> — so whether a text opens a word is
    /// carried from the text before it in the same block, whatever that text's own transform was.
    /// </remarks>
    private string Transformed(string text, TextTransform transform)
    {
        string transformed = transform switch
        {
            TextTransform.Uppercase => text.ToUpperInvariant(),
            TextTransform.Lowercase => text.ToLowerInvariant(),
            TextTransform.Capitalize => Capitalized(text, _isWordStart),
            _ => text,
        };

        _isWordStart = text.Length == 0 ? _isWordStart : IsWordBoundary(text[^1]) || (_isWordStart && IsOpening(text));

        return transformed;
    }

    /// <summary>Whether a text holds nothing that could begin a word, so a word still starts after it.</summary>
    private static bool IsOpening(string text)
    {
        foreach (char character in text)
        {
            if (!IsWordBoundary(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Raises the first letter of every word, leaving the rest of each word as it was typed.</summary>
    /// <remarks>
    /// Casing is invariant rather than cultural: a document carries no locale, and a Turkish machine would
    /// otherwise print a dotless capital in English and Russian text alike.
    /// </remarks>
    /// <remarks>
    /// The first letter of each word is raised, and a word ends only where text does: a space, a dash, an opening
    /// bracket or quote. An apostrophe, a stop inside a word, a combining accent and either half of a surrogate
    /// pair belong to the word they stand in — capitalising after every non-letter wrote «Don'T» and «CaféS».
    /// </remarks>
    private static string Capitalized(string text, bool isWordStart)
    {
        char[] letters = text.ToCharArray();
        bool isStart = isWordStart;

        for (int index = 0; index < letters.Length; index++)
        {
            char letter = letters[index];

            if (isStart && char.IsLetter(letter))
            {
                letters[index] = char.ToUpperInvariant(letter);
                isStart = false;
                continue;
            }

            isStart = IsWordBoundary(letter) || (isStart && !char.IsLetterOrDigit(letter) && !char.IsSurrogate(letter));
        }

        return new string(letters);
    }

    private static bool IsWordBoundary(char character) =>
        !char.IsLetterOrDigit(character)
        && !char.IsSurrogate(character)
        && character is not ('\'' or '\u2019' or '.' or ':' or '\u00B7')
        && CharUnicodeInfo.GetUnicodeCategory(character) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark);

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
        INodeList children = element.ChildNodes;

        for (int index = 0; index < children.Length; index++)
        {
            if (children[index] is not IElement child || !IsTag(child, "aside") || !child.HasAttribute("resumed"))
            {
                continue;
            }

            Style inner = Compute(child, style) with { Display = Display.Block };

            resumed.Add(Block(child, inner, box.ContentLeft, box.ContentWidth));
        }

        return resumed;
    }

    /// <summary>The element a running line's numbers are written into, so their runs can say what they stand for.</summary>
    internal const string FieldTag = "snail-field";

    /// <summary>The attribute of <see cref="FieldTag"/> naming the number: <c>page</c> or <c>pages</c>.</summary>
    internal const string FieldKind = "kind";

    /// <summary>The computed style of an element under its parent's.</summary>
    internal Style Compute(IElement element, Style parent)
    {
        Style style = Styles.Compute(_cascade.Specified(element), parent, _rootFontSize);

        if (!IsTag(element, FieldTag))
        {
            return style;
        }

        return style with { Field = element.GetAttribute(FieldKind) == "pages" ? TextField.PageCount : TextField.PageNumber };
    }

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

        _isWordStart = true;

        if (IsPicture(element))
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
        INodeList children = node.ChildNodes;

        for (int index = 0; index < children.Length; index++)
        {
            INode child = children[index];

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

            if (_depth >= Deepest)
            {
                run.Add(Flattened(element, parent));
                continue;
            }

            _depth++;
            Measure(element, style, run, nested, basis);
            _depth--;
        }
    }

    private void Measure(IElement element, Style style, List<InlineItem> run, List<(float Min, float Max)> nested, float basis)
    {
        if (IsPicture(element))
        {
            Append(run, Picture(element, style, basis));
            return;
        }

        if (IsTag(element, "table"))
        {
            float table = _tables.Width(element, style, basis);

            nested.Add((table, table));
            return;
        }

        if (style.IsBlockLevel)
        {
            nested.Add(Intrinsic(element, style, basis));
            return;
        }

        Gather(element, style, run, nested, basis);
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

        _isWordStart = true;

        if (IsPicture(element))
        {
            segments.Add(Picture(element, inner, width));

            return segments.Close();
        }

        Walk(element, inner, segments);

        return segments.Close();
    }

    /// <summary>Sets the children of a node into the segments of the block they belong to, in order.</summary>
    /// <remarks>
    /// Children are walked by index, here and wherever the layout looks at an element's children: walking a
    /// node list through its interface made an enumerator for every element, and its element collection
    /// made a collection, an iterator and a filter on top.
    /// </remarks>
    private void Walk(INode node, Style parent, Segments segments)
    {
        INodeList children = node.ChildNodes;

        for (int index = 0; index < children.Length; index++)
        {
            INode child = children[index];

            _ct.ThrowIfCancellationRequested();

            if (child is IText text)
            {
                segments.Add(new TextRun(Transformed(text.Data, parent.Transform), parent));
                continue;
            }

            if (child is not IElement element || Compute(element, parent) is not { Display: not Display.None } style)
            {
                continue;
            }

            if (_depth >= Deepest)
            {
                segments.Add(Flattened(element, parent));
                continue;
            }

            _depth++;
            Visit(element, style, segments);
            _depth--;
        }
    }

    /// <summary>An element too deep to follow, as the text it holds, said once per document.</summary>
    private TextRun Flattened(IElement element, Style parent)
    {
        if (!_isCapped)
        {
            _isCapped = true;
            _notes.Add(new Note(
                NoteKind.NestingCapped,
                element.LocalName,
                $"Elements are nested more than {Deepest} deep; what lies below that is set as plain text."));
        }

        return new TextRun(Transformed(TextOf(element), parent.Transform), parent);
    }

    /// <summary>The text an element holds, read with a stack of its own rather than by recursion.</summary>
    /// <remarks>
    /// AngleSharp's own <c>TextContent</c> recurses once per element, so on the very trees this is for it ran out
    /// of stack itself: 11 670 frames deep at 20 000 nested blocks.
    /// </remarks>
    private static string TextOf(IElement element)
    {
        var text = new StringBuilder();
        Stack<INode> waiting = new([element]);

        while (waiting.TryPop(out INode? node))
        {
            if (node is IText run)
            {
                text.Append(run.Data);
                continue;
            }

            INodeList children = node.ChildNodes;

            for (int index = children.Length - 1; index >= 0; index--)
            {
                waiting.Push(children[index]);
            }
        }

        return text.ToString();
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

        if (IsPicture(element))
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

    /// <summary>Whether the element is a picture set as one unit: an <c>img</c>, or an <c>svg</c> drawn in place.</summary>
    /// <remarks>
    /// An <c>svg</c> written into the document is a replaced element exactly as an <c>img</c> is: its text
    /// belongs to the drawing, and walking into it would set the words of its labels as lines of the page.
    /// </remarks>
    private static bool IsPicture(IElement element) => IsTag(element, "img") || IsTag(element, "svg");

    private InlineItem? Picture(IElement element, Style style, float containingWidth)
    {
        bool isDrawing = IsTag(element, "svg");
        string source = isDrawing ? "<svg>" : element.GetAttribute("src") ?? "";
        Image? image = isDrawing ? Svg.Of(element) : _pictures.Read(source);

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

        if (!isFramed || drawnWidth <= 0 || drawnHeight <= 0)
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

    private Note MissingPicture(string source)
    {
        string shown = source.Length > 80 ? $"{source[..77]}..." : source;
        string text = !_pictures.HasSource
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
