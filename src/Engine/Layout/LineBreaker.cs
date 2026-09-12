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

    public IReadOnlyList<LineFlow> Break(IReadOnlyList<InlineItem> items, Style block, float x, float width, Marker? marker)
    {
        var tokens = new Tokens(measurements);

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
        var tokens = new Tokens(measurements);

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
        List<LineText> texts,
        List<LinePicture> pictures,
        List<LineBox> boxes)
    {
        if (token is Glue glue)
        {
            if (stretch == 0 && glue.Width > 0)
            {
                texts.Add(TextOf(" ", cursor, baseline, glue.Width, glue.Style));
                Paint(boxes, cursor, baseline, glue.Width, glue.Style);
            }

            return cursor + glue.Width + stretch;
        }

        foreach (Piece piece in ((Box)token).Pieces)
        {
            if (piece is TextPiece text)
            {
                float on = baseline + Shift(text.Style);

                texts.Add(TextOf(text.Text, cursor, on, text.Width, text.Style));
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

    private static LineText TextOf(string text, float x, float baseline, float width, Style style) =>
        new(text, x, baseline, width, style.Font, style.Color, style.Decoration);

    private static List<LineText> Merge(List<LineText> texts)
    {
        List<LineText> merged = [];

        foreach (LineText text in texts)
        {
            if (merged.Count > 0 && CanJoin(merged[^1], text))
            {
                merged[^1] = merged[^1] with { Text = $"{merged[^1].Text}{text.Text}", Width = merged[^1].Width + text.Width };
                continue;
            }

            merged.Add(text);
        }

        return merged;
    }

    /// <summary>Two runs are one span when they are set alike, sit on one baseline and touch.</summary>
    /// <remarks>
    /// The baseline has to be compared, not only the face: a superscript in the same face as the text beside
    /// it touches that text and matches it in every other way, and joining the two would draw the raised run
    /// back down on the line it was lifted off.
    /// </remarks>
    private static bool CanJoin(LineText left, LineText right) =>
        left.Font == right.Font
        && left.Color == right.Color
        && left.Decoration == right.Decoration
        && Math.Abs(left.Baseline - right.Baseline) < Tolerance
        && Math.Abs(left.X + left.Width - right.X) < Tolerance;

    private LineFlow Build(IReadOnlyList<Token> tokens, Style block, float left, float available, bool isJustifiable, Marker? marker, float contentLeft)
    {
        (float ascent, float descent) = Extent(tokens, block, marker);
        float slack = available - tokens.Sum(token => token.Width);
        int glues = tokens.Count(token => token is Glue);
        bool isJustified = isJustifiable && block.TextAlign == TextAlign.Justify && glues > 0 && slack > 0;
        float stretch = isJustified ? slack / glues : 0;
        float cursor = left + (isJustified ? 0 : Offset(block.TextAlign, Math.Max(0, slack)));

        List<LineText> texts = [];
        List<LinePicture> pictures = [];
        List<LineMark> marks = [];
        List<LineBox> boxes = [];

        if (marker is not null)
        {
            Hang(marker, contentLeft, ascent, texts, marks);
        }

        foreach (Token token in tokens)
        {
            cursor = Set(token, cursor, ascent, stretch, texts, pictures, boxes);
        }

        return new LineFlow(ascent + descent, Merge(texts), pictures, marks, boxes);
    }

    private void Hang(Marker marker, float contentLeft, float baseline, List<LineText> texts, List<LineMark> marks)
    {
        Font font = marker.Style.Font;
        float right = contentLeft - measurements.Measure(" ", font);

        if (marker.Text.Length == 0)
        {
            marks.Add(LineMark.Bullet(marker.Kind, right, baseline, font.Size, marker.Style.Color));
            return;
        }

        float width = measurements.Measure(marker.Text, font);
        texts.Add(new LineText(marker.Text, right - width, baseline, width, font, marker.Style.Color, TextDecoration.None));
    }

    private (float Ascent, float Descent) Extent(IReadOnlyList<Token> tokens, Style block, Marker? marker)
    {
        List<(float Ascent, float Descent)> extents = [Extent(block), .. tokens.SelectMany(Extents)];

        if (marker is not null)
        {
            extents.Add(Extent(marker.Style));
        }

        return (extents.Max(extent => extent.Ascent), extents.Max(extent => extent.Descent));
    }

    private IEnumerable<(float Ascent, float Descent)> Extents(Token token) => token switch
    {
        Glue glue => [Extent(glue.Style)],
        Box box => box.Pieces.Select(Extent),
        _ => [],
    };

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
    private static float Shift(Style style) => style.VerticalAlign switch
    {
        VerticalAlign.Sub => style.Font.Size * 0.2f,
        VerticalAlign.Super => style.Font.Size * -0.33f,
        _ => 0,
    };

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
                    Finish(isJustifiable: false);
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
                Finish(isJustifiable: false);
            }

            return _lines;
        }

        private void Place(Box box)
        {
            float needed = (_pending?.Width ?? 0) + box.Width;

            if (_line.Count > 0 && _used + needed > Available + Tolerance)
            {
                Finish(isJustifiable: true);
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
                    Finish(isJustifiable: false);
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

        private void Finish(bool isJustifiable)
        {
            Marker? lineMarker = _lines.Count == 0 ? marker : null;
            _lines.Add(breaker.Build(_line, block, x + Indent, Available, isJustifiable, lineMarker, x));
            _line.Clear();
            _used = 0;
            _pending = null;
        }
    }
}
