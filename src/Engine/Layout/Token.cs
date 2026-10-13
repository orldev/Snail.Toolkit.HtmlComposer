using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>What a line is filled with: a box that never breaks inside, glue where a line may end, or a forced end.</summary>
/// <remarks>
/// The names are TeX's, because the model is: a line is boxes held apart by glue, and the glue is where the
/// line may be cut and, when the line is justified, where it stretches.
/// </remarks>
internal abstract record Token(float Width);

/// <summary>A place the line may end, as wide as a space or as wide as nothing.</summary>
internal sealed record Glue(Style Style, float Width) : Token(Width);

/// <summary>A run of pieces the line never breaks inside: a word, a picture, the room a margin keeps.</summary>
/// <param name="Pieces">What the box holds, in order.</param>
/// <param name="Width">How far the box advances the line.</param>
/// <param name="IsBreakable">Whether the box may be cut between graphemes once it is wider than a whole line.</param>
/// <remarks>
/// The pieces are an array because every line walks them, and a <c>foreach</c> over an array allocates no
/// enumerator where one over a read-only list allocates one per word.
/// </remarks>
internal sealed record Box(Piece[] Pieces, float Width, bool IsBreakable) : Token(Width);

/// <summary>A line end the markup asked for: a <c>br</c>, or a line feed the white space mode preserves.</summary>
internal sealed record Forced() : Token(0);

/// <summary>One part of a box, set in one style.</summary>
internal abstract record Piece(Style Style, float Width);

/// <summary>Text in one style, measured as it will be drawn.</summary>
internal sealed record TextPiece(string Text, Style Style, float Width) : Piece(Style, Width);

/// <summary>Room in the line that draws nothing: the side margin of an inline element.</summary>
/// <remarks>The placing loop advances the cursor past every piece, so nothing else has to know of it.</remarks>
internal sealed record SpacePiece(Style Style, float Width) : Piece(Style, Width);

/// <summary>A picture in a line: <c>Width</c> is how far it advances the line, <c>Drawn</c> the picture itself.</summary>
internal sealed record PicturePiece(
    Image Image,
    float Height,
    Style Style,
    float Width,
    float SpaceBefore,
    float Drawn,
    Rect? Source) : Piece(Style, Width);
