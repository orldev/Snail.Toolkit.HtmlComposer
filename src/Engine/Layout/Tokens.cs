using System.Globalization;
using System.Text;
using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>Turns inline items into boxes that never break inside and glue where a line may end.</summary>
/// <remarks>
/// Only spaces, tabs and line feeds collapse, as CSS specifies, so a no-break space keeps two words
/// together; a soft hyphen is hidden and a zero-width space is a place to break that draws nothing. A word
/// wider than a whole line is cut between graphemes by <see cref="Chunks"/>, because on paper overflow is
/// lost content rather than a scrollbar. Setting a cell and measuring it go through this one tokeniser, so
/// the two cannot disagree about where a word ends.
/// </remarks>
internal sealed class Tokens(Measurements measurements)
{
    private const char SoftHyphen = (char)0x00AD;

    private const char ZeroWidthSpace = (char)0x200B;

    private readonly List<Token> _tokens = [];

    private readonly List<Piece> _pieces = [];

    private readonly StringBuilder _word = new();

    private Style? _wordStyle;

    private enum Role
    {
        Letter,
        Space,
        LineFeed,
        Opportunity,
        Hidden,
    }

    public void Add(InlineItem item)
    {
        switch (item)
        {
            case TextRun run:
                AddText(run.Text, run.Style);
                break;
            case LineBreak:
                CloseBox();
                _tokens.Add(new Forced());
                break;
            case InlineSpace space:
                FlushWord();
                _pieces.Add(new SpacePiece(space.Style, space.Width));
                break;
            case PictureRun picture:
                FlushWord();
                _pieces.Add(new PicturePiece(
                    picture.Image,
                    picture.Height,
                    picture.Style,
                    picture.SpaceBefore + picture.Width + picture.SpaceAfter,
                    picture.SpaceBefore,
                    picture.Width,
                    picture.Source));
                break;
        }
    }

    public IReadOnlyList<Token> Close()
    {
        CloseBox();

        return _tokens;
    }

    /// <summary>The pieces of an over-long box that fit a line each, cut between graphemes.</summary>
    public IEnumerable<Box> Chunks(Box box, float available)
    {
        var chunk = new Chunk(measurements);

        foreach ((string grapheme, Style style) in Graphemes(box))
        {
            float graphemeWidth = measurements.Measure(grapheme, style.Font);

            if (!chunk.IsEmpty && chunk.Width + graphemeWidth > available + LineBreaker.Tolerance)
            {
                yield return chunk.ToBox();
                chunk = new Chunk(measurements);
            }

            chunk.Add(grapheme, style, graphemeWidth);
        }

        if (!chunk.IsEmpty)
        {
            yield return chunk.ToBox();
        }
    }

    private static Role Classify(char character, WhiteSpace mode) => character switch
    {
        SoftHyphen => Role.Hidden,
        ZeroWidthSpace => Role.Opportunity,
        '\r' => PreservesLineFeeds(mode) ? Role.Hidden : Role.Space,
        '\n' => PreservesLineFeeds(mode) ? Role.LineFeed : Role.Space,
        ' ' or '\t' or '\f' => Role.Space,
        _ => Role.Letter,
    };

    private static bool PreservesLineFeeds(WhiteSpace mode) => mode is WhiteSpace.Pre or WhiteSpace.PreWrap or WhiteSpace.PreLine;

    private static IEnumerable<(string Grapheme, Style Style)> Graphemes(Box box)
    {
        foreach (TextPiece piece in box.Pieces.OfType<TextPiece>())
        {
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(piece.Text);

            while (elements.MoveNext())
            {
                yield return (elements.GetTextElement(), piece.Style);
            }
        }
    }

    private void AddText(string text, Style style)
    {
        foreach (char character in text)
        {
            AddCharacter(character, style);
        }

        FlushWord();
    }

    private void AddCharacter(char character, Style style)
    {
        switch (Classify(character, style.WhiteSpace))
        {
            case Role.Letter:
                AppendToWord(character.ToString(), style);
                break;
            case Role.Space:
                AddSpace(character, style);
                break;
            case Role.LineFeed:
                CloseBox();
                _tokens.Add(new Forced());
                break;
            case Role.Opportunity:
                CloseBox();
                _tokens.Add(new Glue(style, 0));
                break;
        }
    }

    private void AddSpace(char character, Style style)
    {
        string preserved = character == '\t' ? "        " : " ";

        switch (style.WhiteSpace)
        {
            case WhiteSpace.Pre:
                AppendToWord(preserved, style);
                break;
            case WhiteSpace.PreWrap:
                FlushWord();
                _pieces.Add(new TextPiece(preserved, style, measurements.Measure(preserved, style.Font)));
                CloseBox();
                _tokens.Add(new Glue(style, 0));
                break;
            case WhiteSpace.NoWrap:
                AddUnbreakableSpace(style);
                break;
            default:
                AddCollapsibleSpace(style);
                break;
        }
    }

    private void AddUnbreakableSpace(Style style)
    {
        FlushWord();

        if (_pieces is [.., TextPiece { Text: not " " }] or [.., PicturePiece])
        {
            _pieces.Add(new TextPiece(" ", style, measurements.Measure(" ", style.Font)));
        }
    }

    private void AddCollapsibleSpace(Style style)
    {
        CloseBox();

        if (_tokens is not [.., Glue])
        {
            _tokens.Add(new Glue(style, measurements.Measure(" ", style.Font)));
        }
    }

    private void AppendToWord(string text, Style style)
    {
        if (_wordStyle is not null && !ReferenceEquals(_wordStyle, style))
        {
            FlushWord();
        }

        _wordStyle = style;
        _word.Append(text);
    }

    private void FlushWord()
    {
        if (_word.Length == 0 || _wordStyle is null)
        {
            return;
        }

        string text = _word.ToString();
        _pieces.Add(new TextPiece(text, _wordStyle, measurements.Measure(text, _wordStyle.Font)));
        _word.Clear();
    }

    private void CloseBox()
    {
        FlushWord();

        if (_pieces.Count == 0)
        {
            return;
        }

        bool isBreakable = _pieces.All(piece => piece is TextPiece { Style.WhiteSpace: not (WhiteSpace.Pre or WhiteSpace.NoWrap) });
        _tokens.Add(new Box([.. _pieces], _pieces.Sum(piece => piece.Width), isBreakable));
        _pieces.Clear();
    }

    /// <summary>The graphemes of an over-long word that fit on one line.</summary>
    private sealed class Chunk(Measurements measurements)
    {
        private readonly List<(StringBuilder Text, Style Style)> _parts = [];

        public float Width { get; private set; }

        public bool IsEmpty => _parts.Count == 0;

        public void Add(string grapheme, Style style, float width)
        {
            if (_parts.Count == 0 || !ReferenceEquals(_parts[^1].Style, style))
            {
                _parts.Add((new StringBuilder(), style));
            }

            _parts[^1].Text.Append(grapheme);
            Width += width;
        }

        public Box ToBox()
        {
            List<Piece> pieces =
            [
                .. _parts.Select(part => new TextPiece(part.Text.ToString(), part.Style, measurements.Measure(part.Text.ToString(), part.Style.Font))),
            ];

            return new Box(pieces, pieces.Sum(piece => piece.Width), IsBreakable: false);
        }
    }
}
