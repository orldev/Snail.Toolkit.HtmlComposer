namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>How the lines of a paragraph sit between its edges.</summary>
public enum TextAlignment
{
    /// <summary>Against the left edge.</summary>
    Left,

    /// <summary>Against the right edge.</summary>
    Right,

    /// <summary>Centred between the edges.</summary>
    Center,

    /// <summary>Stretched to both edges, all but the last line.</summary>
    Justify,
}

/// <summary>One line of a paragraph as it was set on the page.</summary>
/// <param name="Top">The top of the line box on the page.</param>
/// <param name="Height">The height of the line box.</param>
/// <param name="Spans">The runs of the line; the same objects as in <see cref="Page.Spans"/>.</param>
/// <param name="IsWrappedAtSpace">
/// Whether the line ran out of room at a space, which the break swallowed; joining it to the next line takes a
/// space back between them. A line parted inside a word joins its successor directly.
/// </param>
public sealed record TextLine(float Top, float Height, IReadOnlyList<TextSpan> Spans, bool IsWrappedAtSpace);

/// <summary>A paragraph: lines that break only where they ran out of room, ended by a forced break or by its block.</summary>
/// <param name="Alignment">How its block aligns the lines.</param>
/// <param name="Lines">The lines in reading order.</param>
public sealed record TextParagraph(TextAlignment Alignment, IReadOnlyList<TextLine> Lines);

/// <summary>A block of text on a page — a heading, a paragraph, a caption — as paragraphs rather than placed runs.</summary>
/// <param name="Bounds">The content box of the block on this page: where its lines may run.</param>
/// <param name="Paragraphs">The paragraphs in reading order.</param>
/// <remarks>
/// The runs in <see cref="Page.Spans"/> say where every word was drawn, which is all a fixed page needs. A
/// renderer whose text flows again on its own — a deck, where a text box wraps its lines as it is edited —
/// needs to know which runs make one paragraph and where that paragraph may run, and this says so. The runs
/// are the same objects in both lists, so a renderer that draws a frame knows which runs it has drawn.
/// <para>
/// A frame is made only for text the engine can give back whole: the lines of one block that hold nothing but
/// text. A block whose lines carry pictures or painted inline boxes keeps its runs placed one by one, and a
/// list marker hanging outside its block is not part of the paragraph.
/// </para>
/// </remarks>
public sealed record TextFrame(Rect Bounds, IReadOnlyList<TextParagraph> Paragraphs);
