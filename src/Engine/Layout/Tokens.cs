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

    private char[] _word = new char[32];

    private int _wordLength;

    private Style? _wordStyle;

    private Glue? _space;

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

    /// <summary>This tokeniser emptied for the next run of inline content, its lists and its word buffer kept.</summary>
    /// <remarks>
    /// A breaker sets one run at a time and walks its tokens to the end before it takes the next, so one
    /// tokeniser serves it throughout: a new one for every paragraph grew a token list, a piece list and a
    /// word buffer from nothing each time, a ninth of what a long document allocated.
    /// </remarks>
    public Tokens Cleared()
    {
        _tokens.Clear();
        _pieces.Clear();
        _wordLength = 0;
        _wordStyle = null;

        return this;
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
                AppendToWord(character, style);
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
            _tokens.Add(SpaceIn(style));
        }
    }

    /// <summary>The glue of a space between words of one style, made once for as long as the style lasts.</summary>
    /// <remarks>
    /// Every space of a paragraph is the same glue, as wide as a space of its face, and nothing tells one from
    /// another: a glue for each of them was a tenth of what a long document allocated.
    /// </remarks>
    private Glue SpaceIn(Style style)
    {
        if (_space is null || !ReferenceEquals(_space.Style, style))
        {
            _space = new Glue(style, measurements.Measure(" ", style.Font));
        }

        return _space;
    }

    private void AppendToWord(string text, Style style)
    {
        EnterWordStyle(style);
        Room(text.Length);
        text.CopyTo(_word.AsSpan(_wordLength));
        _wordLength += text.Length;
    }

    /// <summary>Adds one letter to the word in progress.</summary>
    /// <remarks>Every letter of a document comes through here, and a string per letter was a fifteenth of what a long one allocated.</remarks>
    private void AppendToWord(char character, Style style)
    {
        EnterWordStyle(style);
        Room(1);
        _word[_wordLength++] = character;
    }

    private void EnterWordStyle(Style style)
    {
        if (_wordStyle is not null && !ReferenceEquals(_wordStyle, style))
        {
            FlushWord();
        }

        _wordStyle = style;
    }

    private void FlushWord()
    {
        if (_wordLength == 0 || _wordStyle is null)
        {
            return;
        }

        string text = measurements.Word(_word.AsSpan(0, _wordLength), _wordStyle.Font, out float width);
        _pieces.Add(new TextPiece(text, _wordStyle, width));
        _wordLength = 0;
    }

    /// <summary>Makes room in the word buffer for as many more letters.</summary>
    private void Room(int letters)
    {
        if (_wordLength + letters > _word.Length)
        {
            Array.Resize(ref _word, Math.Max(_word.Length * 2, _wordLength + letters));
        }
    }

    /// <summary>Ends the word in progress and the box it belongs to.</summary>
    /// <remarks>
    /// Width and breakability are taken in one pass over the pieces. The width is summed in <see cref="double"/>,
    /// as <c>Enumerable.Sum</c> summed it before, so a box is exactly as wide as it was.
    /// </remarks>
    private void CloseBox()
    {
        FlushWord();

        if (_pieces.Count == 0)
        {
            return;
        }

        Piece[] pieces = [.. _pieces];
        double width = 0;
        bool isBreakable = true;

        foreach (Piece piece in pieces)
        {
            width += piece.Width;
            isBreakable &= piece is TextPiece { Style.WhiteSpace: not (WhiteSpace.Pre or WhiteSpace.NoWrap) };
        }

        _tokens.Add(new Box(pieces, (float)width, isBreakable));
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
            Piece[] pieces =
            [
                .. _parts.Select(part => new TextPiece(part.Text.ToString(), part.Style, measurements.Measure(part.Text.ToString(), part.Style.Font))),
            ];

            return new Box(pieces, pieces.Sum(piece => piece.Width), IsBreakable: false);
        }
    }
}
