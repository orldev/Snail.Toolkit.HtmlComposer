using System.Text;
using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>Breaks inline content into lines that fit a width, first fit, the way browsers do.</summary>
/// <remarks>
/// <see cref="Tokens"/> turns the content into boxes and glue. The lines are filled first fit: a box goes on
/// the current line if it fits and opens the next if not, and a box wider than a whole line is cut between
/// graphemes rather than left to run off the page. Each line is then built: its runs on one baseline, as tall
/// as the tallest of them asks, justified or aligned as the block says.
/// </remarks>
internal sealed class LineBreaker(Measurements measurements, CancellationToken ct)
{
    internal const float Tolerance = 0.01f;

    private readonly LineTexts _texts = new();

    private readonly Tokens _tokens = new(measurements);

    public IReadOnlyList<LineFlow> Break(IReadOnlyList<InlineItem> items, Style block, float x, float width, Marker? marker)
    {
        Tokens tokens = _tokens.Cleared();

        foreach (InlineItem item in items)
        {
            ct.ThrowIfCancellationRequested();

            tokens.Add(item);
        }

        var fill = new Fill(this, tokens, block, x, width, marker);

        foreach (Token token in tokens.Close())
        {
            ct.ThrowIfCancellationRequested();

            fill.Add(token);
        }

        return fill.End();
    }

    /// <summary>The narrowest and the widest the content can be set: its longest word, and one unbroken line.</summary>
    /// <remarks>
    /// Automatic table layout has to know what a cell wants before it can hand out the width it gets, so the
    /// content is measured twice: once here and once for real, when the column is settled. Both passes go
    /// through the same tokeniser, so they cannot disagree about where a word ends. A forced break ends a line
    /// here as it does there, which is why the widest is taken per line rather than over the whole run.
    /// </remarks>
    public (float Min, float Max) Intrinsic(IReadOnlyList<InlineItem> items)
    {
        Tokens tokens = _tokens.Cleared();

        foreach (InlineItem item in items)
        {
            ct.ThrowIfCancellationRequested();

            tokens.Add(item);
        }

        float word = 0;
        float widest = 0;
        float line = 0;

        foreach (Token token in tokens.Close())
        {
            ct.ThrowIfCancellationRequested();

            if (token is Forced)
            {
                widest = Math.Max(widest, line);
                line = 0;
                continue;
            }

            line += token.Width;
            word = token is Box box ? Math.Max(word, box.Width) : word;
        }

        return (word, Math.Max(widest, line));
    }

    private static float Offset(TextAlign align, float slack) => align switch
    {
        TextAlign.Right => slack,
        TextAlign.Center => slack / 2,
        _ => 0,
    };

    private float Set(
        Token token,
        float cursor,
        float baseline,
        float stretch,
        List<LinePicture> pictures,
        List<LineBox> boxes)
    {
        if (token is Glue glue)
        {
            if (stretch == 0 && glue.Width > 0)
            {
                _texts.Add(" ", cursor, baseline, glue.Width, glue.Style);
                Paint(boxes, cursor, baseline, glue.Width, glue.Style);
            }

            return cursor + glue.Width + stretch;
        }

        foreach (Piece piece in ((Box)token).Pieces)
        {
            if (piece is TextPiece text)
            {
                float on = baseline + Shift(text.Style);

                _texts.Add(text.Text, cursor, on, text.Width, text.Style);
                Paint(boxes, cursor, on, text.Width, text.Style);
            }

            if (piece is PicturePiece picture)
            {
                pictures.Add(new LinePicture(
                    cursor + picture.SpaceBefore,
                    baseline - Above(picture),
                    picture.Drawn,
                    picture.Height,
                    picture.Image,
                    picture.Source));

                Frame(boxes, cursor + picture.SpaceBefore, baseline - Above(picture), picture.Drawn, picture.Height, picture.Style);
            }

            cursor += piece.Width;
        }

        return cursor;
    }

    /// <summary>Puts a box behind a run of text that asks to be painted, as tall as that run's own line.</summary>
    private void Paint(List<LineBox> boxes, float x, float baseline, float width, Style style)
    {
        if (!IsPainted(style))
        {
            return;
        }

        (float above, float below) = Extent(style);

        boxes.Add(new LineBox(x, baseline - above, width, above + below, style.Background, style.Border, style.BorderRadius));
    }

    /// <summary>Puts a box around a picture: its own rectangle, not the height of the line it sits in.</summary>
    /// <remarks>
    /// A picture is not text and has no line of its own to be as tall as. Giving it the line's height would
    /// draw its border away from its edges, which is what a rule around a photograph must never do.
    /// <para>
    /// The box stands outside the picture by the width of its border, as a border stands outside the content
    /// it frames. Sharing the picture's own rectangle would put the stroke under it: boxes are painted before
    /// pictures, so the picture would cover the very rule that was asked for. The line is not widened by it.
    /// </para>
    /// </remarks>
    private static void Frame(List<LineBox> boxes, float x, float top, float width, float height, Style style)
    {
        if (!IsPainted(style))
        {
            return;
        }

        Edges edges = style.Border.Widths;

        boxes.Add(new LineBox(
            x - edges.Left,
            top - edges.Top,
            width + edges.Horizontal,
            height + edges.Vertical,
            style.Background,
            style.Border,
            style.BorderRadius));
    }

    private static bool IsPainted(Style style) => style.Background is { IsVisible: true } || style.Border.IsVisible;

    private LineFlow Build(IReadOnlyList<Token> tokens, Style block, float left, float available, LineEnd end, Marker? marker, float contentLeft)
    {
        (float ascent, float descent) = Extent(tokens, block, marker);
        double used = 0;
        int glues = 0;

        for (int index = 0; index < tokens.Count; index++)
        {
            used += tokens[index].Width;
            glues += tokens[index] is Glue ? 1 : 0;
        }

        float slack = available - (float)used;
        bool isJustified = end is LineEnd.Space or LineEnd.Word && block.TextAlign == TextAlign.Justify && glues > 0 && slack > 0;
        float stretch = isJustified ? slack / glues : 0;
        float cursor = left + (isJustified ? 0 : Offset(block.TextAlign, Math.Max(0, slack)));

        List<LinePicture> pictures = [];
        List<LineMark> marks = [];
        List<LineBox> boxes = [];

        if (marker is not null)
        {
            Hang(marker, contentLeft, ascent, marks);
        }

        for (int index = 0; index < tokens.Count; index++)
        {
            cursor = Set(tokens[index], cursor, ascent, stretch, pictures, boxes);
        }

        return new LineFlow(ascent + descent, _texts.Close(), pictures, marks, boxes) { End = end, Align = block.TextAlign };
    }

    private void Hang(Marker marker, float contentLeft, float baseline, List<LineMark> marks)
    {
        Font font = marker.Style.Font;
        float right = contentLeft - measurements.Measure(" ", font);

        if (marker.Text.Length == 0)
        {
            marks.Add(LineMark.Bullet(marker.Kind, right, baseline, font.Size, marker.Style.Color));
            return;
        }

        float width = measurements.Measure(marker.Text, font);
        _texts.Add(marker.Text, right - width, baseline, width, font, marker.Style.Color, TextDecoration.None, isMarker: true);
    }

    /// <summary>How far the line reaches above and below its baseline: the most any of its runs asks for.</summary>
    /// <remarks>
    /// A running maximum over the block, every run and the marker, in that order, rather than a list of them
    /// all: the list, the query over it and an array per space were among the largest allocations of a long document.
    /// </remarks>
    private (float Ascent, float Descent) Extent(IReadOnlyList<Token> tokens, Style block, Marker? marker)
    {
        (float Ascent, float Descent) line = Extent(block);

        for (int index = 0; index < tokens.Count; index++)
        {
            if (tokens[index] is Glue glue)
            {
                line = Taller(line, Extent(glue.Style));
            }

            if (tokens[index] is Box box)
            {
                foreach (Piece piece in box.Pieces)
                {
                    line = Taller(line, Extent(piece));
                }
            }
        }

        return marker is null ? line : Taller(line, Extent(marker.Style));
    }

    private static (float Ascent, float Descent) Taller((float Ascent, float Descent) line, (float Ascent, float Descent) run) =>
        (Larger(line.Ascent, run.Ascent), Larger(line.Descent, run.Descent));

    /// <summary>The larger of two extents, as <c>Enumerable.Max</c> decides it.</summary>
    /// <remarks>A NaN gives way to any value and never wins over one, so the line is as tall as it was.</remarks>
    private static float Larger(float current, float candidate) => float.IsNaN(current) || candidate > current ? candidate : current;

    private (float Ascent, float Descent) Extent(Piece piece)
    {
        if (piece is PicturePiece picture)
        {
            float above = Above(picture);

            return (above, picture.Height - above);
        }

        (float ascent, float descent) = Extent(piece.Style);
        float shift = Shift(piece.Style);

        return (ascent - Math.Min(0, shift), descent + Math.Max(0, shift));
    }

    /// <summary>How far the baseline of a run moves for <c>sub</c> and <c>super</c>, downwards being positive.</summary>
    /// <remarks>
    /// Both the height of the line and the placing of the run go through this, so a raised run can never be
    /// drawn above the room the line was given for it. CSS fixes no distance; a fifth of the em down and a
    /// third up are the usual ones, and they are taken from the run's own size rather than its parent's, so a
    /// smaller face raised in a larger one moves by its own measure.
    /// </remarks>
    private static float Shift(Style style) => style.Shift;

    /// <summary>How far above the baseline the top of a picture sits, by its <c>vertical-align</c>.</summary>
    /// <remarks>
    /// Making the line tall enough and placing the picture in it both go through this, so a picture is never
    /// drawn where the line was not made room for it. <c>middle</c> centres the picture on the middle of the
    /// lower case: a face's metrics carry no x-height, and half the em is the usual stand-in for it.
    /// </remarks>
    private float Above(PicturePiece picture)
    {
        FontMetrics metrics = measurements.MetricsOf(picture.Style.Font);

        return picture.Style.VerticalAlign switch
        {
            VerticalAlign.Middle => (picture.Height / 2) + (picture.Style.Font.Size / 4),
            VerticalAlign.TextTop => metrics.Ascent,
            VerticalAlign.TextBottom => picture.Height - metrics.Descent,
            _ => picture.Height,
        };
    }

    private (float Ascent, float Descent) Extent(Style style)
    {
        FontMetrics metrics = measurements.MetricsOf(style.Font);
        float lineHeight = style.LineHeight?.Resolve(style.Font.Size) ?? metrics.NormalLineHeight;
        float halfLeading = (lineHeight - metrics.Ascent - metrics.Descent) / 2;

        return (metrics.Ascent + halfLeading, metrics.Descent + halfLeading);
    }

    /// <summary>The texts of one line as they are set, each run of touching texts set alike joined into one span.</summary>
    /// <remarks>
    /// Joined as the line is set: a text was made for every word and every space, listed, and then joined run by
    /// run, and those texts and their lists were nearly a third of what a long document allocated. A text joins the
    /// run when it is set as the run's first text is, sits on its baseline and touches its end, the widths
    /// being added in the order the texts come — what joining the list afterwards did, so the spans are the
    /// same to the last bit. The baseline is compared and not only the face: a superscript touches the text
    /// beside it and matches it in every other way, and joining the two would draw it back down on the line.
    /// One instance serves a breaker, which sets one line at a time.
    /// </remarks>
    private sealed class LineTexts
    {
        private readonly List<LineText> _set = [];

        private readonly StringBuilder _joined = new();

        private string _text = string.Empty;

        private int _count;

        private float _x;

        private float _baseline;

        private float _width;

        private Font? _font;

        private Rgba _color;

        private TextDecoration _decoration;

        private TextField _field;

        private bool _isMarker;

        public void Add(string text, float x, float baseline, float width, Style style) =>
            Add(text, x, baseline, width, style.Font, style.Color, style.Decoration, style.Field);

        public void Add(string text, float x, float baseline, float width, Font font, Rgba color, TextDecoration decoration, TextField field = TextField.None, bool isMarker = false)
        {
            if (_count > 0 && !isMarker && !_isMarker && Touches(x, baseline, font, color, decoration, field))
            {
                if (_count == 1)
                {
                    _joined.Append(_text);
                }

                _joined.Append(text);
                _width += width;
                _count++;

                return;
            }

            End();

            _text = text;
            _x = x;
            _baseline = baseline;
            _width = width;
            _font = font;
            _color = color;
            _decoration = decoration;
            _field = field;
            _isMarker = isMarker;
            _count = 1;
        }

        /// <summary>The spans of the line, and a clean start for the next one.</summary>
        public LineText[] Close()
        {
            End();

            LineText[] texts = [.. _set];
            _set.Clear();

            return texts;
        }

        private bool Touches(float x, float baseline, Font font, Rgba color, TextDecoration decoration, TextField field) =>
            _field == field
            && _font == font
            && _color == color
            && _decoration == decoration
            && Math.Abs(_baseline - baseline) < Tolerance
            && Math.Abs(_x + _width - x) < Tolerance;

        private void End()
        {
            if (_count == 0)
            {
                return;
            }

            _set.Add(new LineText(_count == 1 ? _text : _joined.ToString(), _x, _baseline, _width, _font!, _color, _decoration) { Field = _field, IsMarker = _isMarker });
            _joined.Clear();
            _count = 0;
        }
    }

    /// <summary>Fills lines first fit: a box goes on the current line if it fits, otherwise it opens the next.</summary>
    private sealed class Fill(LineBreaker breaker, Tokens tokens, Style block, float x, float width, Marker? marker)
    {
        private readonly List<LineFlow> _lines = [];

        private readonly List<Token> _line = [];

        private Glue? _pending;

        private float _used;

        private float Indent => _lines.Count == 0 ? block.TextIndent.Resolve(width) : 0;

        private float Available => width - Indent;

        public void Add(Token token)
        {
            switch (token)
            {
                case Glue glue when _line.Count > 0:
                    _pending ??= glue;
                    break;
                case Forced:
                    Finish(LineEnd.Forced);
                    break;
                case Box box:
                    Place(box);
                    break;
            }
        }

        public List<LineFlow> End()
        {
            if (_line.Count > 0)
            {
                Finish(LineEnd.Last);
            }

            return _lines;
        }

        private void Place(Box box)
        {
            float needed = (_pending?.Width ?? 0) + box.Width;

            if (_line.Count > 0 && _used + needed > Available + Tolerance)
            {
                Finish(_pending is null ? LineEnd.Word : LineEnd.Space);
            }

            if (_line.Count == 0 && box.Width > Available + Tolerance && box.IsBreakable)
            {
                PlaceChunks(box);
                return;
            }

            Append(box);
        }

        private void PlaceChunks(Box box)
        {
            foreach (Box chunk in tokens.Chunks(box, Available))
            {
                if (_line.Count > 0)
                {
                    Finish(LineEnd.Cut);
                }

                Append(chunk);
            }
        }

        private void Append(Box box)
        {
            if (_line.Count > 0 && _pending is not null)
            {
                _line.Add(_pending);
                _used += _pending.Width;
            }

            _line.Add(box);
            _used += box.Width;
            _pending = null;
        }

        private void Finish(LineEnd end)
        {
            Marker? lineMarker = _lines.Count == 0 ? marker : null;
            _lines.Add(breaker.Build(_line, block, x + Indent, Available, end, lineMarker, x));
            _line.Clear();
            _used = 0;
            _pending = null;
        }
    }
}
